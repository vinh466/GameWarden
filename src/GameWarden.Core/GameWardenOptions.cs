namespace GameWarden.Core;

/// <summary>Section <c>GameWarden</c> of the configuration (docs/08-operations.md §6).</summary>
public sealed class GameWardenOptions
{
    public const string Section = "GameWarden";

    /// <summary>Radmin VPN (26.0.0.0/8) and the CGNAT range used by Tailscale and others; used when <see cref="VpnRanges"/> is empty.</summary>
    public static readonly IReadOnlyList<string> DefaultVpnRanges = ["26.0.0.0/8", "100.64.0.0/10"];

    /// <summary>Data root (docs/08-operations.md §1).</summary>
    public string DataRoot { get; set; } = @"C:\GameWarden";

    public int PanelPort { get; set; } = 8765;

    /// <summary>Runner executable; defaults to the one next to Core.</summary>
    public string? RunnerPath { get; set; }

    /// <summary>Built-in adapters, read-only (OQ-18); defaults to <c>adapters-builtin</c> next to Core.</summary>
    public string? BuiltinAdaptersPath { get; set; }

    /// <summary>Loopback CLI token; defaults to <c>C:\ProgramData\GameWarden\cli.token</c>.</summary>
    public string? CliTokenPath { get; set; }

    /// <summary>
    /// <c>auto</c> (on when Core runs as LocalSystem, i.e. as the service), <c>on</c> or <c>off</c>: the run-as user,
    /// the data-root ACL and firewall rules (ADR-0008, ADR-0009, 08 §7). Development and CI run with it off (02 §11).
    /// </summary>
    public string Isolation { get; set; } = "auto";

    public string RunAsUserName { get; set; } = "gamewarden-run";

    /// <summary>Remote addresses allowed by the firewall rules of game ports (08 §7).</summary>
    public string[] VpnRanges { get; set; } = [];

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

    public string ResolvedSteamCmdPath => SteamCmdPath ?? Path.Combine(SteamCmdFolder, "steamcmd.exe");

    public IReadOnlyList<string> ResolvedVpnRanges => VpnRanges.Length > 0 ? VpnRanges : DefaultVpnRanges;

    public IReadOnlyList<string> ResolvedSteamCmdDownloadUrls =>
        SteamCmdDownloadUrl is { } configured
            ? [configured, .. DefaultSteamCmdDownloadUrls.Where(url => !string.Equals(url, configured, StringComparison.OrdinalIgnoreCase))]
            : DefaultSteamCmdDownloadUrls;

    public string ResolvedRunnerPath => RunnerPath ?? Path.Combine(AppContext.BaseDirectory, "GameWarden.Runner.exe");

    public string ResolvedBuiltinAdaptersPath => BuiltinAdaptersPath ?? Path.Combine(AppContext.BaseDirectory, "adapters-builtin");

    public string ResolvedCliTokenPath =>
        CliTokenPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameWarden", "cli.token");

    /// <summary>The data root before the options are built (logging and Owner overrides need it early).</summary>
    public static string ReadDataRoot(IConfiguration configuration) =>
        Environment.ExpandEnvironmentVariables(configuration[$"{Section}:{nameof(DataRoot)}"] ?? @"C:\GameWarden");
}
