namespace GameWarden.Core.Instances;

/// <summary>Folders of one instance (docs/08-operations.md §1). Runner state lives under <c>logs\runner</c>, which gamewarden-run may write (ADR-0009).</summary>
public sealed record InstancePaths(string Root)
{
    public static InstancePaths For(string dataRoot, string instanceId) => new(Path.Combine(dataRoot, "instances", instanceId));

    public string Server => Path.Combine(Root, "server");

    public string Config => Path.Combine(Root, "config");

    public string Data => Path.Combine(Root, "data");

    public string Mods => Path.Combine(Root, "mods");

    public string Logs => Path.Combine(Root, "logs");

    public string RunnerState => Path.Combine(Logs, "runner");

    public string LaunchSpecFile => Path.Combine(RunnerState, "launchSpec.json");

    /// <summary>The folders gamewarden-run may modify (ADR-0009 §1).</summary>
    public IReadOnlyList<string> RunAsFolders => [Server, Config, Data, Mods, Logs];

    /// <summary>Creates the instance folder and its five sub-folders; anything deeper is created as gamewarden-run.</summary>
    public void Create()
    {
        Directory.CreateDirectory(Root);
        foreach (var folder in RunAsFolders)
        {
            Directory.CreateDirectory(folder);
        }
    }
}
