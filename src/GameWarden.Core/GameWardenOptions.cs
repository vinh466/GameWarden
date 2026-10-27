namespace GameWarden.Core;

/// <summary>Section <c>GameWarden</c> of the configuration (docs/08-operations.md §6).</summary>
public sealed class GameWardenOptions
{
    public const string Section = "GameWarden";

    /// <summary>Data root (docs/08-operations.md §1).</summary>
    public string DataRoot { get; set; } = @"C:\GameWarden";

    public int PanelPort { get; set; } = 8765;

    /// <summary>Runner executable; defaults to the one next to Core.</summary>
    public string? RunnerPath { get; set; }

    /// <summary>Built-in adapters, read-only (OQ-18); defaults to <c>adapters-builtin</c> next to Core.</summary>
    public string? BuiltinAdaptersPath { get; set; }

    /// <summary>Loopback CLI token; defaults to <c>C:\ProgramData\GameWarden\cli.token</c>.</summary>
    public string? CliTokenPath { get; set; }

    public string DatabasePath => Path.Combine(DataRoot, "db", "gamewarden.db");

    public string OwnerAdaptersPath => Path.Combine(DataRoot, "adapters");

    public string ResolvedRunnerPath => RunnerPath ?? Path.Combine(AppContext.BaseDirectory, "GameWarden.Runner.exe");

    public string ResolvedBuiltinAdaptersPath => BuiltinAdaptersPath ?? Path.Combine(AppContext.BaseDirectory, "adapters-builtin");

    public string ResolvedCliTokenPath =>
        CliTokenPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameWarden", "cli.token");
}
