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

    /// <summary>Config files Core renders before every start (ADR-0022 decision 1).</summary>
    public IReadOnlyList<AdapterFile> Files { get; init; } = [];

    public AdapterReadiness? Readiness { get; init; }

    public AdapterStop? Stop { get; init; }

    public AdapterBackup? Backup { get; init; }

    public AdapterResources? Resources { get; init; }

    /// <summary>Mod support (L3, 04 §3, docs/13 §4.1): the loader Core uses and where packages come from.</summary>
    public AdapterMods? Mods { get; init; }

    /// <summary>Ids from <see cref="Checks.Prerequisites.Known"/>, checked before a start (08 §2).</summary>
    public IReadOnlyList<string> Prerequisites { get; init; } = [];

    /// <summary><c>settings.schema.json</c> next to <c>adapter.json</c> (L2, 04 §5); never part of <c>adapter.json</c> itself.</summary>
    [JsonIgnore]
    public SettingsSchema? Schema { get; init; }

    /// <summary><c>help.json</c> next to <c>adapter.json</c> (04 §5.3): optional, and only with a settings schema.</summary>
    [JsonIgnore]
    public AdapterHelp? Help { get; init; }

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

/// <param name="Internal">Allocated and checked for clashes like any other port, but no firewall rule: only Core uses it, on loopback (ADR-0022 decision 4).</param>
public sealed record AdapterPort(string Name, string Protocol, int? Default = null, PortOffset? Offset = null, bool Internal = false);

public sealed record PortOffset(string Of, int By);

/// <summary>
/// <c>logRegex</c> (with <c>source</c> <c>stdout</c> or <c>file:&lt;path&gt;</c>, OQ-19), <c>delay</c> (<c>delaySec</c>),
/// <c>port</c> (ready once something listens on the adapter port named <c>port</c>) or <c>http</c> (ready once the URL
/// answers <c>status</c>, every <c>intervalSec</c>; ADR-0022 decision 3).
/// </summary>
public sealed record AdapterReadiness(
    string Type,
    string? Source = null,
    string? Pattern = null,
    int? DelaySec = null,
    string? Port = null,
    int TimeoutSec = 300,
    string? Url = null,
    int? Status = null,
    AdapterAuth? Auth = null,
    int? IntervalSec = null);

/// <summary>HTTP basic auth; both values are templates, so the password is <c>{secrets.AdminPassword}</c> (ADR-0022).</summary>
public sealed record AdapterAuth(string Username, string Password);

/// <summary><c>ctrl-c</c>, <c>stdin</c>, <c>rcon</c>, <c>rest</c>, <c>http</c> or <c>kill</c> (02 §4.3).</summary>
public sealed record AdapterStop(
    string Method,
    string? Command = null,
    int TimeoutSec = 60,
    IReadOnlyList<AdapterHttpRequest>? Requests = null,
    AdapterAuth? Auth = null);

/// <summary>One request of an <c>http</c> stop, in order; a failure moves on to waiting for the exit (ADR-0022 decision 3).</summary>
public sealed record AdapterHttpRequest(string Method, string Url, string? Body = null, int TimeoutSec = 10);

/// <summary>
/// A config file Core renders from the game's own template before every start. <c>Format</c> is <c>ue-struct-ini</c>
/// (an Unreal <c>Key=(A=1,B="x")</c> line in an INI section, ADR-0022 decision 1); <c>Fixed</c> values are raw and may
/// use templates, so a secret is written as <c>"\"{secrets.AdminPassword}\""</c>.
/// </summary>
public sealed record AdapterFile(
    string Name,
    string Path,
    string Template,
    string Format,
    string? Section = null,
    string? Field = null,
    IReadOnlyDictionary<string, string>? Fixed = null);

public sealed record AdapterBackup(IReadOnlyList<string> Paths, string Mode);

public sealed record AdapterResources(ResourceAmounts? Min = null, ResourceAmounts? Recommended = null);

public sealed record ResourceAmounts(int? CpuCores = null, int? RamMb = null, int? DiskMb = null);

/// <param name="Loader">Id of a loader Core has (<see cref="Mods.IModLoader"/>), e.g. <c>bepinex</c>.</param>
/// <param name="LoaderPackage">The loader's own package (<c>Namespace-Name</c>), a dependency of every set.</param>
public sealed record AdapterMods(string Loader, string? LoaderPackage = null, IReadOnlyList<AdapterModSource>? Sources = null);

/// <summary><c>thunderstore</c> with its <c>community</c> (e.g. <c>valheim</c>); <c>manual</c> and <c>workshop</c> come later.</summary>
public sealed record AdapterModSource(string Type, string? Community = null);
