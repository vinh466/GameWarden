using System.Text.Json.Serialization;

namespace GameWarden.Core.Adapters;

/// <summary><c>adapter.json</c>, docs/04-adapter-spec.md §3 (the parts implemented so far).</summary>
public sealed record AdapterDefinition
{
    public int SchemaVersion { get; init; }

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Level { get; init; } = string.Empty;

    public AdapterSource? Source { get; init; }

    public AdapterLaunch? Launch { get; init; }

    public IReadOnlyList<AdapterPort> Ports { get; init; } = [];

    public AdapterReadiness? Readiness { get; init; }

    public AdapterStop? Stop { get; init; }

    public AdapterBackup? Backup { get; init; }

    public AdapterResources? Resources { get; init; }

    /// <summary>Ids from <see cref="Checks.Prerequisites.Known"/>, checked before a start (08 §2).</summary>
    public IReadOnlyList<string> Prerequisites { get; init; } = [];

    /// <summary><c>settings.schema.json</c> next to <c>adapter.json</c> (L2, 04 §5); never part of <c>adapter.json</c> itself.</summary>
    [JsonIgnore]
    public SettingsSchema? Schema { get; init; }

    /// <summary>Folder the adapter was loaded from; <c>{paths.adapter}</c> in templates.</summary>
    public string Folder { get; init; } = string.Empty;

    public bool IsBuiltIn { get; init; }
}

public sealed record AdapterSource(string Type, int? AppId = null, string? Url = null);

/// <param name="ConsoleLogFile">File the game writes its console to instead of stdout (Unity's <c>-logFile</c>); the Runner relays it.</param>
public sealed record AdapterLaunch(
    string Exe,
    IReadOnlyList<string>? Args = null,
    string? WorkingDir = null,
    IReadOnlyDictionary<string, string>? Env = null,
    string? ConsoleLogFile = null);

public sealed record AdapterPort(string Name, string Protocol, int? Default = null, PortOffset? Offset = null);

public sealed record PortOffset(string Of, int By);

/// <summary>
/// <c>logRegex</c> (with <c>source</c> <c>stdout</c> or <c>file:&lt;path&gt;</c>, OQ-19), <c>delay</c> (<c>delaySec</c>)
/// or <c>port</c> (ready once something listens on the adapter port named <c>port</c>).
/// </summary>
public sealed record AdapterReadiness(
    string Type,
    string? Source = null,
    string? Pattern = null,
    int? DelaySec = null,
    string? Port = null,
    int TimeoutSec = 300);

/// <summary><c>ctrl-c</c>, <c>stdin</c>, <c>rcon</c>, <c>rest</c> or <c>kill</c>.</summary>
public sealed record AdapterStop(string Method, string? Command = null, int TimeoutSec = 60);

public sealed record AdapterBackup(IReadOnlyList<string> Paths, string Mode);

public sealed record AdapterResources(ResourceAmounts? Min = null, ResourceAmounts? Recommended = null);

public sealed record ResourceAmounts(int? CpuCores = null, int? RamMb = null, int? DiskMb = null);
