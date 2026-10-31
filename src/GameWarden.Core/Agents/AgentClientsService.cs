using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GameWarden.Core.Data;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Agents;

/// <summary>
/// Agent clients and their tokens (docs/15). A client is the audited identity; tokens are credentials that can be
/// rotated or revoked without losing the client's history.
/// </summary>
public sealed class AgentClientsService(
    IDbContextFactory<GameWardenDb> db,
    AgentSettingsService settings,
    AgentUsageTracker usage,
    TimeProvider time)
{
    public const string TokenPrefix = "gw_agent_";
    public const int MaxTokenLifetimeDays = 90;
    public const int DefaultTokenLifetimeDays = 30;

    public async Task<IReadOnlyList<AgentClientView>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var records = await context.AgentClients.AsNoTracking()
            .OrderBy(client => client.Name)
            .ToListAsync(cancellationToken);
        return [.. records.Select(AgentClientView.From)];
    }

    public async Task<AgentClientView?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentClients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == id, cancellationToken) is { } record
            ? AgentClientView.From(record)
            : null;
    }

    public async Task<AgentClientView> CreateAsync(CreateAgentClientRequest request, CancellationToken cancellationToken)
    {
        var name = CheckName(request.Name);
        var permissions = CheckPermissions(request.Permissions, request.TrustedFull == true);
        var networks = AgentNetwork.Normalize(request.AllowedNetworks, "allowedNetworks");
        var now = time.GetUtcNow();

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        if (await context.AgentClients.AnyAsync(client => client.Name == name, cancellationToken))
        {
            throw new AgentRequestException(StatusCodes.Status409Conflict, "agent_name_taken", $"An Agent client named '{name}' exists already.");
        }

        var record = new AgentClientRecord
        {
            Id = "agt_" + Guid.NewGuid().ToString("N")[..10],
            Name = name,
            Description = Clean(request.Description, 256),
            Enabled = true,
            TrustedFull = request.TrustedFull == true,
            PermissionsJson = AgentJson.Write(permissions),
            AllowedNetworksJson = AgentJson.Write(networks),
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.AgentClients.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return AgentClientView.From(record);
    }

    public async Task<AgentClientView?> UpdateAsync(string id, UpdateAgentClientRequest request, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var record = await context.AgentClients.SingleOrDefaultAsync(client => client.Id == id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (request.Name is { } name)
        {
            var checkedName = CheckName(name);
            if (await context.AgentClients.AnyAsync(client => client.Id != id && client.Name == checkedName, cancellationToken))
            {
                throw new AgentRequestException(StatusCodes.Status409Conflict, "agent_name_taken", $"An Agent client named '{checkedName}' exists already.");
            }

            record.Name = checkedName;
        }

        if (request.Description is not null)
        {
            record.Description = Clean(request.Description, 256);
        }

        if (request.Enabled is { } enabled)
        {
            record.Enabled = enabled;
        }

        if (request.TrustedFull is { } trusted)
        {
            record.TrustedFull = trusted;
            if (trusted)
            {
                record.PermissionsJson = AgentJson.Write(AgentPermissions.All.Select(permission => permission.Key));
            }
        }

        if (request.Permissions is { } permissions)
        {
            var checkedPermissions = CheckPermissions(permissions, record.TrustedFull);
            record.PermissionsJson = AgentJson.Write(checkedPermissions);
        }

        if (request.AllowedNetworks is { } networks)
        {
            record.AllowedNetworksJson = AgentJson.Write(AgentNetwork.Normalize(networks, "allowedNetworks"));
        }

        record.UpdatedAt = time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return AgentClientView.From(record);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentClients.Where(client => client.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<AgentTokenView>> ListTokensAsync(string clientId, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var records = await context.AgentTokens.AsNoTracking()
            .Where(token => token.ClientId == clientId)
            .ToListAsync(cancellationToken);
        // SQLite cannot ORDER BY a DateTimeOffset; a client has only a handful of tokens.
        return [.. records.OrderByDescending(token => token.CreatedAt).Select(AgentTokenView.From)];
    }

    public async Task<AgentTokenCreated> CreateTokenAsync(string clientId, CreateAgentTokenRequest request, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        if (!await context.AgentClients.AnyAsync(client => client.Id == clientId, cancellationToken))
        {
            throw new AgentRequestException(StatusCodes.Status404NotFound, "not_found", $"No Agent client '{clientId}'.");
        }

        var token = NewToken(clientId, request, time.GetUtcNow(), out var record);
        context.AgentTokens.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return new AgentTokenCreated(token, AgentTokenView.From(record));
    }

    public async Task<AgentTokenCreated?> RotateAsync(string clientId, string tokenId, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var old = await context.AgentTokens.SingleOrDefaultAsync(token => token.Id == tokenId && token.ClientId == clientId, cancellationToken);
        if (old is null)
        {
            return null;
        }

        old.RevokedAt = time.GetUtcNow();
        var token = NewToken(clientId, new CreateAgentTokenRequest(old.Name, null), old.RevokedAt.Value, out var record);
        context.AgentTokens.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return new AgentTokenCreated(token, AgentTokenView.From(record));
    }

    public async Task<bool> RevokeTokenAsync(string clientId, string tokenId, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var token = await context.AgentTokens.SingleOrDefaultAsync(row => row.Id == tokenId && row.ClientId == clientId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.RevokedAt ??= time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Checks a plaintext <c>gw_agent_...</c> token and returns the client it belongs to.</summary>
    public async Task<AgentPrincipal> AuthenticateAsync(string token, IPAddress? remoteIp, string? version, CancellationToken cancellationToken)
    {
        var parsed = Parse(token);
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var record = await context.AgentTokens.AsNoTracking().SingleOrDefaultAsync(row => row.Id == parsed.Id, cancellationToken);
        if (record is null || !FixedMatch(record.SecretHash, parsed.Hash))
        {
            throw new AgentRequestException(StatusCodes.Status401Unauthorized, "unauthenticated", "The Agent token is missing or wrong.");
        }

        var now = time.GetUtcNow();
        if (record.RevokedAt is not null)
        {
            throw new AgentRequestException(StatusCodes.Status401Unauthorized, "token_revoked", "This Agent token was revoked.");
        }

        if (record.ExpiresAt <= now)
        {
            throw new AgentRequestException(StatusCodes.Status401Unauthorized, "token_expired", "This Agent token has expired.");
        }

        var client = await context.AgentClients.AsNoTracking().SingleOrDefaultAsync(row => row.Id == record.ClientId, cancellationToken);
        if (client is null || !client.Enabled)
        {
            throw new AgentRequestException(StatusCodes.Status403Forbidden, "agent_disabled", "This Agent client is disabled.");
        }

        var feature = await settings.GetAsync(cancellationToken);
        if (!feature.Enabled)
        {
            throw new AgentRequestException(StatusCodes.Status403Forbidden, "agent_access_disabled", "Agent access is disabled in GameWarden.");
        }

        var clientNetworks = AgentJson.Strings(client.AllowedNetworksJson);
        var featureNetworks = AgentJson.Strings(feature.AllowedNetworksJson);
        var allowed = clientNetworks.Count > 0 ? clientNetworks : featureNetworks;
        if (!AgentNetwork.Accepts(allowed, remoteIp))
        {
            throw new AgentRequestException(StatusCodes.Status403Forbidden, "ip_not_allowed", "This Agent token is not allowed from this address.");
        }

        var permissions = client.TrustedFull
            ? AgentPermissions.Names
            : AgentJson.Strings(client.PermissionsJson).Where(AgentPermissions.IsKnown).ToHashSet(StringComparer.Ordinal);
        usage.Track(new AgentUsage(client.Id, record.Id, now, remoteIp?.ToString(), version));

        return new AgentPrincipal(
            client.Id,
            client.Name,
            record.Id,
            record.ExpiresAt,
            client.TrustedFull,
            permissions,
            AgentSettingsService.IsLive(feature.DiagnosticsEnabled, feature.DiagnosticsExpiresAt, now),
            feature.DiagnosticsExpiresAt,
            AgentSettingsService.IsLive(feature.AdminEnabled, feature.AdminExpiresAt, now),
            feature.AdminExpiresAt,
            feature.RequireAdminMode);
    }

    public static string CheckName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 64 || name.Any(character => !(char.IsLetterOrDigit(character) || character is ' ' or '.' or '_' or '-')))
        {
            throw new AgentRequestException(
                StatusCodes.Status400BadRequest,
                ProblemCodes.ValidationFailed,
                "An Agent name is 1 to 64 letters, digits, spaces, dots, dashes or underscores.");
        }

        return name;
    }

    public static IReadOnlyList<string> CheckPermissions(IReadOnlyList<string>? values, bool trustedFull)
    {
        if (trustedFull)
        {
            return AgentPermissions.All.Select(permission => permission.Key).ToArray();
        }

        var permissions = values?.Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (permissions.FirstOrDefault(permission => !AgentPermissions.IsKnown(permission)) is { } unknown)
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"Unknown Agent permission '{unknown}'.");
        }

        return permissions;
    }

    private static string NewToken(string clientId, CreateAgentTokenRequest request, DateTimeOffset now, out AgentTokenRecord record)
    {
        var days = Math.Clamp(request.ExpiresInDays ?? DefaultTokenLifetimeDays, 1, MaxTokenLifetimeDays);
        var id = Guid.NewGuid().ToString("N")[..10];
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = TokenPrefix + id + "_" + secret;
        record = new AgentTokenRecord
        {
            Id = id,
            ClientId = clientId,
            Name = Clean(request.Name, 64) ?? "token",
            SecretHash = Hash(token),
            Prefix = TokenPrefix + id,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
        };
        return token;
    }

    private static (string Id, string Hash) Parse(string token)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            throw new AgentRequestException(StatusCodes.Status401Unauthorized, "unauthenticated", "The Agent token is missing or wrong.");
        }

        var rest = token[TokenPrefix.Length..];
        var separator = rest.IndexOf('_');
        if (separator < 6 || separator > 32
            || rest[..separator].Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            || rest[(separator + 1)..].Length < 20)
        {
            throw new AgentRequestException(StatusCodes.Status401Unauthorized, "unauthenticated", "The Agent token is missing or wrong.");
        }

        return (rest[..separator], Hash(token));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedMatch(string expected, string actual)
    {
        // Token values are high entropy; SHA-256 is enough.
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim().ReplaceLineEndings(" ");
        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }
}

public sealed record AgentPrincipal(
    string ClientId,
    string ClientName,
    string TokenId,
    DateTimeOffset TokenExpiresAt,
    bool TrustedFull,
    IReadOnlySet<string> Permissions,
    bool DiagnosticsLive,
    DateTimeOffset? DiagnosticsExpiresAt,
    bool AdminLive,
    DateTimeOffset? AdminExpiresAt,
    bool RequireAdminMode)
{
    public const string ItemKey = "gw.agent";

    public bool Allows(string permission) => TrustedFull || Permissions.Contains(permission);
}
