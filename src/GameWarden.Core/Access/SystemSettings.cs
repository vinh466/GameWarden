using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameWarden.Core.Hosting;
using GameWarden.Core.Network;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Access;

/// <summary>Who may reach the panel and the game ports (docs/11 §13); a list is null when the default applies.</summary>
public sealed record SystemSettingsView(
    IReadOnlyList<string> PanelAccessNetworks,
    bool PanelAccessNetworksDefault,
    IReadOnlyList<string> VpnRanges,
    bool VpnRangesDefault,
    IReadOnlyList<string> DefaultVpnRanges);

/// <summary>Body of <c>PUT /system/settings</c>: null or empty goes back to the default.</summary>
public sealed record SystemSettingsUpdate(IReadOnlyList<string>? PanelAccessNetworks, IReadOnlyList<string>? VpnRanges);

public sealed class SystemSettingsRefusedException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;
}

/// <summary>
/// The Owner's network settings (docs/11 §13): written to <c>gamewarden.json</c> in the data root, where the Owner
/// could also edit them by hand, and applied at once — the panel's address filter, its firewall rule and every
/// game-port rule. A change that would shut out the browser making it is refused.
/// </summary>
public sealed class SystemSettings(
    IOptions<GameWardenOptions> options,
    PanelNetwork network,
    IFirewall firewall,
    Supervisor supervisor,
    IsolationMode isolation,
    ILogger<SystemSettings> logger)
{
    private readonly object _gate = new();

    public SystemSettingsView Get()
    {
        var o = options.Value;
        return new SystemSettingsView(
            o.ResolvedPanelAccessNetworks,
            o.PanelAccessNetworks.Length == 0,
            o.ResolvedVpnRanges,
            o.VpnRanges.Length == 0,
            GameWardenOptions.DefaultVpnRanges);
    }

    /// <param name="requestArrivedAt">The local address the Owner's request came in on; it must stay reachable.</param>
    public SystemSettingsView Update(SystemSettingsUpdate update, IPAddress? requestArrivedAt)
    {
        var panel = Normalize(update.PanelAccessNetworks);
        var vpn = Normalize(update.VpnRanges);
        lock (_gate)
        {
            var o = options.Value;
            var resolvedVpn = vpn ?? GameWardenOptions.DefaultVpnRanges.ToArray();
            var resolvedPanel = panel ?? resolvedVpn;
            if (requestArrivedAt is not null
                && !PanelNetwork.IsLoopback(requestArrivedAt)
                && !new PanelNetwork(resolvedPanel, []).AcceptsLocal(requestArrivedAt))
            {
                throw new SystemSettingsRefusedException(
                    StatusCodes.Status409Conflict,
                    "would_lock_out",
                    $"This browser reaches the panel at {PanelNetwork.Normalize(requestArrivedAt)}, which the new panel networks leave out: you would lock yourself out. Keep a range that contains it, or change this on the GameWarden machine itself.");
            }

            Write(panel, vpn);

            // The options object is the one every service reads, so changing it reaches them all at once.
            o.PanelAccessNetworks = panel ?? [];
            o.VpnRanges = vpn ?? [];
            network.Update(o.ResolvedPanelAccessNetworks, o.PanelHostNames);
            if (isolation.Enabled)
            {
                firewall.SetPanel(o.PanelPort, o.ResolvedPanelAccessNetworks);
                supervisor.ReapplyFirewall();
            }

            logger.LogInformation(
                "Network settings changed: panel {Panel}, VPN ranges {Vpn}.",
                string.Join(", ", o.ResolvedPanelAccessNetworks),
                string.Join(", ", o.ResolvedVpnRanges));
            return Get();
        }
    }

    /// <summary>Null and empty mean "the default"; anything else must be CIDR ranges.</summary>
    private static string[]? Normalize(IReadOnlyList<string>? ranges)
    {
        var trimmed = ranges?.Select(range => range.Trim()).Where(range => range.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (trimmed is null || trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.FirstOrDefault(range => !IPNetwork.TryParse(range, out _)) is { } bad)
        {
            throw new SystemSettingsRefusedException(StatusCodes.Status400BadRequest, "validation_failed", $"'{bad}' is not a range like 26.0.0.0/8.");
        }

        return trimmed;
    }

    /// <summary>Keeps everything else in the file; writes a temporary file first so a crash cannot leave half a file.</summary>
    private void Write(string[]? panel, string[]? vpn)
    {
        var path = Path.Combine(options.Value.DataRoot, "gamewarden.json");
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];
        if (root[GameWardenOptions.Section] is not JsonObject section)
        {
            root[GameWardenOptions.Section] = section = [];
        }

        Set(section, nameof(GameWardenOptions.PanelAccessNetworks), panel);
        Set(section, nameof(GameWardenOptions.VpnRanges), vpn);
        var temporary = path + ".new";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static void Set(JsonObject section, string name, string[]? ranges)
    {
        if (ranges is null)
        {
            section.Remove(name);
        }
        else
        {
            section[name] = new JsonArray([.. ranges.Select(range => (JsonNode)range)]);
        }
    }
}
