using GameWarden.Contracts;

namespace GameWarden.Core;

/// <summary>Section <c>GameWarden</c> of the configuration (docs/08-operations.md §6).</summary>
public sealed class GameWardenOptions
{
    public const string Section = "GameWarden";

    /// <summary>Radmin VPN (26.0.0.0/8) and the CGNAT range used by Tailscale and others; used when <see cref="VpnRanges"/> is empty.</summary>
    public static readonly IReadOnlyList<string> DefaultVpnRanges = ["26.0.0.0/8", "100.64.0.0/10"];

    /// <summary>Data root (docs/08-operations.md §1); <see cref="DefaultPaths.DataRoot"/> when not configured.</summary>
    public string DataRoot { get; set; } = DefaultPaths.DataRoot;

    public int PanelPort { get; set; } = 8765;

    /// <summary>Runner executable; defaults to the one next to Core (<c>GameWarden.Runner.exe</c> on Windows).</summary>
    public string? RunnerPath { get; set; }

    /// <summary>Built-in adapters, read-only (OQ-18); defaults to <c>adapters-builtin</c> next to Core.</summary>
    public string? BuiltinAdaptersPath { get; set; }

    /// <summary>Loopback CLI token; defaults to <see cref="DefaultPaths.CliToken"/> (<c>C:\ProgramData\GameWarden\cli.token</c> on Windows).</summary>
    public string? CliTokenPath { get; set; }

    /// <summary>
    /// <c>auto</c> (on when Core runs as LocalSystem, i.e. as the service), <c>on</c> or <c>off</c>: the run-as user,
    /// the data-root ACL and firewall rules (ADR-0008, ADR-0009, 08 §7). Development and CI run with it off (02 §11);
    /// it exists on Windows only, so <c>on</c> elsewhere stops Core at start (ADR-0016).
    /// </summary>
    public string Isolation { get; set; } = "auto";

    public string RunAsUserName { get; set; } = "gamewarden-run";

    /// <summary>Remote addresses allowed by the firewall rules of game ports (08 §7).</summary>
    public string[] VpnRanges { get; set; } = [];

    /// <summary>
    /// Local networks the panel answers on besides loopback (06 §2, OQ-28): a connection is accepted only when the
    /// address it arrived at is inside one. Defaults to the VPN ranges; a LAN or public range is the Owner's explicit choice.
    /// </summary>
    public string[] PanelAccessNetworks { get; set; } = [];

    /// <summary>
    /// Host names the panel may be opened by besides <c>localhost</c> and IP addresses (e.g. a Tailscale MagicDNS name);
    /// any other <c>Host</c> header is refused, against DNS rebinding (05 §1, OQ-22).
    /// </summary>
    public string[] PanelHostNames { get; set; } = [];

    /// <summary>One-time setup token for creating the Owner; defaults to <c>setup.token</c> next to the CLI token.</summary>
    public string? SetupTokenPath { get; set; }

    public string? SteamCmdPath { get; set; }

    /// <summary>
    /// SteamCMD for Windows, tried in this order; a configured <see cref="SteamCmdDownloadUrl"/> goes first. Valve's wiki
    /// names the akamaihd address, but some ISPs' DNS answer it with 127.0.0.1 (seen in Vietnam, 2026-10-28, 08 §2);
    /// media.steampowered.com serves the same zip.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSteamCmdDownloadUrls =
    [
        "https://media.steampowered.com/client/installer/steamcmd.zip",
        "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip",
    ];

    /// <summary>Owner override for the SteamCMD download (gamewarden.json); the defaults are still tried after it.</summary>
    public string? SteamCmdDownloadUrl { get; set; }

    /// <summary>
    /// Most bytes one mod package may unpack to in a set build (docs/13 §4.5), counted as they are written; a package
    /// over it fails the <c>mods.sync</c> job. 2 GiB by default; Epic Loot 0.15.4, the largest of the group's mods,
    /// unpacks to about 53 MB.
    /// </summary>
    public long ModPackageMaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>GitHub repository the updater reads releases from (ADR-0013).</summary>
    public string UpdateRepository { get; set; } = "vinh466/GameWarden";

    public string DatabasePath => Path.Combine(DataRoot, "db", "gamewarden.db");

    public string OwnerAdaptersPath => Path.Combine(DataRoot, "adapters");

    public string InstancesPath => Path.Combine(DataRoot, "instances");

    public string SecretsPath => Path.Combine(DataRoot, "secrets");

    public string ToolsPath => Path.Combine(DataRoot, "tools");

    public string SteamCmdFolder => Path.Combine(ToolsPath, "steamcmd");

    public string CoreLogsPath => Path.Combine(DataRoot, "logs", "core");

    public string JobLogsPath => Path.Combine(DataRoot, "logs", "jobs");

    public string UpdatesPath => Path.Combine(DataRoot, "updates");

    /// <summary>
    /// Core's scratch folder (<c>tmp\</c>), which only SYSTEM can write (ADR-0009 §1): mod downloads land in
    /// <c>tmp\mods\&lt;job id&gt;\</c> before they are hashed and copied into an instance (docs/13 §4.5).
    /// </summary>
    public string TempPath => Path.Combine(DataRoot, "tmp");

    /// <summary>
    /// ASP.NET Core Data Protection keys (session cookies, CSRF tokens), encrypted with DPAPI on Windows; on the
    /// development platform protected by file permissions only (ADR-0016).
    /// </summary>
    public string KeysPath => Path.Combine(DataRoot, "keys");

    public string ResolvedSteamCmdPath => SteamCmdPath ?? Path.Combine(SteamCmdFolder, "steamcmd.exe");

    public IReadOnlyList<string> ResolvedVpnRanges => VpnRanges.Length > 0 ? VpnRanges : DefaultVpnRanges;

    public IReadOnlyList<string> ResolvedPanelAccessNetworks => PanelAccessNetworks.Length > 0 ? PanelAccessNetworks : ResolvedVpnRanges;

    public IReadOnlyList<string> ResolvedSteamCmdDownloadUrls =>
        SteamCmdDownloadUrl is { } configured
            ? [configured, .. DefaultSteamCmdDownloadUrls.Where(url => !string.Equals(url, configured, StringComparison.OrdinalIgnoreCase))]
            : DefaultSteamCmdDownloadUrls;

    public string ResolvedRunnerPath =>
        RunnerPath ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "GameWarden.Runner.exe" : "GameWarden.Runner");

    public string ResolvedBuiltinAdaptersPath => BuiltinAdaptersPath ?? Path.Combine(AppContext.BaseDirectory, "adapters-builtin");

    public string ResolvedCliTokenPath => CliTokenPath ?? DefaultPaths.CliToken;

    public string ResolvedSetupTokenPath =>
        SetupTokenPath ?? Path.Combine(Path.GetDirectoryName(ResolvedCliTokenPath)!, "setup.token");

    /// <summary>The data root before the options are built (logging and Owner overrides need it early).</summary>
    public static string ReadDataRoot(IConfiguration configuration) =>
        Environment.ExpandEnvironmentVariables(configuration[$"{Section}:{nameof(DataRoot)}"] ?? DefaultPaths.DataRoot);
}
