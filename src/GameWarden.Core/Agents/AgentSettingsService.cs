using System.Net;
using GameWarden.Core.Data;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Agents;

/// <summary>
/// The one Agent feature row (docs/15): global switch, time-boxed diagnostics/admin windows and default networks.
/// The Owner changes it; an Agent can never change its own elevation.
/// </summary>
public sealed class AgentSettingsService(IDbContextFactory<GameWardenDb> db, TimeProvider time)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AgentSettingsRecord> GetAsync(CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentSettings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == 1, cancellationToken)
            ?? new AgentSettingsRecord { UpdatedAt = time.GetUtcNow() };
    }

    public async Task<AgentSettingsView> ViewAsync(CancellationToken cancellationToken) =>
        AgentSettingsView.From(await GetAsync(cancellationToken), time.GetUtcNow());

    public async Task<AgentSettingsView> UpdateAsync(UpdateAgentSettingsRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            var record = await context.AgentSettings.SingleOrDefaultAsync(row => row.Id == 1, cancellationToken);
            if (record is null)
            {
                record = new AgentSettingsRecord { UpdatedAt = time.GetUtcNow() };
                context.AgentSettings.Add(record);
            }

            var now = time.GetUtcNow();
            if (request.Enabled is { } enabled)
            {
                record.Enabled = enabled;
                if (!enabled)
                {
                    record.DiagnosticsEnabled = false;
                    record.DiagnosticsExpiresAt = null;
                    record.AdminEnabled = false;
                    record.AdminExpiresAt = null;
                }
            }

            if (request.RequireAdminMode is { } requireAdmin)
            {
                record.RequireAdminMode = requireAdmin;
            }

            if (request.Diagnostics is { } diagnostics)
            {
                ApplyWindow(diagnostics, "Diagnostics", now, out var expires, out var reason);
                record.DiagnosticsEnabled = diagnostics.Enabled;
                record.DiagnosticsExpiresAt = expires;
                record.DiagnosticsReason = reason;
            }

            if (request.Admin is { } admin)
            {
                ApplyWindow(admin, "Admin", now, out var expires, out var reason);
                record.AdminEnabled = admin.Enabled;
                record.AdminExpiresAt = expires;
                record.AdminReason = reason;
            }

            if (request.AllowedNetworks is { } networks)
            {
                record.AllowedNetworksJson = AgentJson.Write(AgentNetwork.Normalize(networks, "allowedNetworks"));
            }

            record.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return AgentSettingsView.From(record, now);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A mode window that has passed is off, even if the row still says enabled.</summary>
    public static bool IsLive(bool enabled, DateTimeOffset? expiresAt, DateTimeOffset now) => enabled && expiresAt > now;

    private static void ApplyWindow(AgentModeRequest request, string name, DateTimeOffset now, out DateTimeOffset? expiresAt, out string? reason)
    {
        if (!request.Enabled)
        {
            expiresAt = null;
            reason = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"{name} mode needs a reason.");
        }

        var ttl = request.TtlMinutes ?? (name == "Admin" ? 30 : 30);
        var max = name == "Admin" ? 60 : 480;
        if (ttl is < 1 or > 480 || (name == "Admin" && ttl > max))
        {
            throw new AgentRequestException(
                StatusCodes.Status400BadRequest,
                ProblemCodes.ValidationFailed,
                $"{name} mode lasts 1 to {max} minutes.");
        }

        expiresAt = now.AddMinutes(ttl);
        reason = request.Reason.Trim();
    }
}

public static class AgentNetwork
{
    /// <summary>Trimmed, distinct CIDR ranges; throws a validation error naming the first bad entry.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? ranges, string field)
    {
        var trimmed = ranges?.Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (trimmed.FirstOrDefault(value => !IPNetwork.TryParse(value, out _)) is { } bad)
        {
            throw new AgentRequestException(
                StatusCodes.Status400BadRequest,
                ProblemCodes.ValidationFailed,
                $"'{bad}' in {field} is not a CIDR range like 100.64.0.10/32.");
        }

        return trimmed;
    }

    public static bool Accepts(IReadOnlyList<string> ranges, IPAddress? address)
    {
        if (ranges.Count == 0)
        {
            return true;
        }

        if (address is null)
        {
            return false;
        }

        address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return ranges.Any(range => IPNetwork.TryParse(range, out var network) && network.Contains(address));
    }
}

public sealed class AgentRequestException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;
}
