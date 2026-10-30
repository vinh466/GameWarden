using System.Text.Json;
using System.Text.RegularExpressions;
using GameWarden.Contracts;
using GameWarden.Core.Mods;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Adapters;

/// <summary>
/// Loads adapters: built-in ones read-only from the install folder, the Owner's from the data root,
/// where the same <c>id</c> overrides a built-in adapter (OQ-18). Invalid adapters are listed with
/// their errors and cannot be used (docs/04-adapter-spec.md §8). A <c>mods</c> section must name a loader Core has.
/// </summary>
public sealed partial class AdapterCatalog(IOptions<GameWardenOptions> options, ILogger<AdapterCatalog> logger, IEnumerable<IModLoader>? loaders = null)
{
    private static readonly string[] Levels = ["L0", "L1", "L2", "L3"];
    private static readonly string[] StopMethods = ["ctrl-c", "stdin", "rcon", "rest", "kill"];
    private static readonly string[] ReadinessTypes = ["logRegex", "delay", "port"];
    private static readonly string[] ModSourceTypes = ["thunderstore"];

    private readonly IReadOnlyList<string> _loaderIds = [.. (loaders ?? []).Select(loader => loader.Id)];

    private IReadOnlyDictionary<string, AdapterDefinition> _valid = new Dictionary<string, AdapterDefinition>();
    private IReadOnlyList<AdapterProblem> _problems = [];

    public IReadOnlyList<AdapterDefinition> Adapters => [.. _valid.Values.OrderBy(adapter => adapter.Id, StringComparer.Ordinal)];

    public IReadOnlyList<AdapterProblem> Problems => _problems;

    public AdapterDefinition? Find(string id) => _valid.GetValueOrDefault(id);

    public void Reload()
    {
        var found = new Dictionary<string, AdapterDefinition>(StringComparer.Ordinal);
        var problems = new List<AdapterProblem>();
        Load(options.Value.ResolvedBuiltinAdaptersPath, builtIn: true, found, problems);
        Load(options.Value.OwnerAdaptersPath, builtIn: false, found, problems);
        _valid = found;
        _problems = problems;
        foreach (var problem in problems)
        {
            logger.LogWarning("Adapter {Folder} is invalid: {Errors}", problem.Folder, string.Join("; ", problem.Errors));
        }
    }

    /// <param name="today">For the dates in <c>help.json</c>; by default today where the calendar is furthest ahead.</param>
    /// <param name="loaders">Ids of the mod loaders Core has; none by default.</param>
    public static IReadOnlyList<string> Validate(AdapterDefinition adapter, DateOnly? today = null, IReadOnlyCollection<string>? loaders = null)
    {
        var errors = new List<string>();
        if (adapter.SchemaVersion != 1)
        {
            errors.Add($"schemaVersion must be 1 (was {adapter.SchemaVersion}).");
        }

        if (!AdapterId().IsMatch(adapter.Id))
        {
            errors.Add("id must match [a-z0-9-]+.");
        }

        if (string.IsNullOrWhiteSpace(adapter.Name))
        {
            errors.Add("name is required.");
        }

        if (string.IsNullOrWhiteSpace(adapter.Version))
        {
            errors.Add("version is required.");
        }

        if (!Levels.Contains(adapter.Level))
        {
            errors.Add("level must be L0, L1, L2 or L3.");
            return errors;
        }

        errors.AddRange(AdapterHelpValidation.Errors(adapter, today ?? AdapterHelpValidation.LatestToday(TimeProvider.System)));

        if (adapter.Level == "L0")
        {
            return errors;
        }

        if (adapter.Source is null)
        {
            errors.Add("source is required from L1.");
        }

        if (adapter.Launch is null || string.IsNullOrWhiteSpace(adapter.Launch.Exe))
        {
            errors.Add("launch.exe is required from L1.");
        }

        if (adapter.Readiness is null || !ReadinessTypes.Contains(adapter.Readiness.Type))
        {
            errors.Add("readiness.type must be logRegex, delay or port.");
        }
        else if (adapter.Readiness.Type == "logRegex")
        {
            if (string.IsNullOrEmpty(adapter.Readiness.Pattern))
            {
                errors.Add("readiness.pattern is required for logRegex.");
            }

            if (adapter.Readiness.Source is { } source && source != "stdout" && !source.StartsWith("file:", StringComparison.Ordinal))
            {
                errors.Add("readiness.source must be stdout or file:<path>.");
            }
        }

        if (adapter.Stop is null || !StopMethods.Contains(adapter.Stop.Method))
        {
            errors.Add("stop.method must be ctrl-c, stdin, rcon, rest or kill.");
        }
        else if (adapter.Stop.Method == "stdin" && string.IsNullOrEmpty(adapter.Stop.Command))
        {
            errors.Add("stop.command is required for the stdin method.");
        }

        if (adapter.Backup is null || adapter.Backup.Paths.Count == 0)
        {
            errors.Add("backup.paths is required from L1.");
        }

        if (adapter.Resources?.Min is null)
        {
            errors.Add("resources.min is required from L1 (OQ-07).");
        }

        // L2 means a settings form (04 §1); the schema and the level go together.
        if (adapter.Level is "L2" or "L3" && adapter.Schema is null)
        {
            errors.Add("settings.schema.json is required from L2.");
        }
        else if (adapter.Level == "L1" && adapter.Schema is not null)
        {
            errors.Add("an adapter with settings.schema.json is L2; set level to L2.");
        }
        else if (adapter.Schema is not null)
        {
            errors.AddRange(SettingsSchemaValidation.Errors(adapter));
        }

        // L3 means a Mods tab (04 §1); the section and the level go together, as the schema and L2 do.
        if (adapter.Level == "L3" && adapter.Mods is null)
        {
            errors.Add("mods is required at L3.");
        }
        else if (adapter.Level != "L3" && adapter.Mods is not null)
        {
            errors.Add("an adapter with mods is L3; set level to L3.");
        }
        else if (adapter.Mods is { } mods)
        {
            errors.AddRange(ModsErrors(mods, loaders ?? []));
        }

        foreach (var prerequisite in adapter.Prerequisites.Where(id => !Checks.Prerequisites.Known.Contains(id)))
        {
            errors.Add($"prerequisites: unknown id '{prerequisite}' (known: {string.Join(", ", Checks.Prerequisites.Known)}).");
        }

        foreach (var port in adapter.Ports)
        {
            if (port.Protocol is not ("udp" or "tcp"))
            {
                errors.Add($"port {port.Name}: protocol must be udp or tcp.");
            }

            if (port.Default is null && port.Offset is null)
            {
                errors.Add($"port {port.Name}: default or offset is required.");
            }
        }

        return errors;
    }

    private static IEnumerable<string> ModsErrors(AdapterMods mods, IReadOnlyCollection<string> loaders)
    {
        if (string.IsNullOrWhiteSpace(mods.Loader))
        {
            yield return "mods.loader is required.";
        }
        else if (!loaders.Contains(mods.Loader))
        {
            yield return loaders.Count == 0
                ? $"mods.loader: unknown loader '{mods.Loader}' (this version of Core has no mod loader)."
                : $"mods.loader: unknown loader '{mods.Loader}' (known: {string.Join(", ", loaders)}).";
        }

        if (mods.LoaderPackage is not null && !Packages.IsPackage(mods.LoaderPackage))
        {
            yield return "mods.loaderPackage must be a package name, Namespace-Name.";
        }

        if (mods.Sources is not { Count: > 0 } sources)
        {
            yield return "mods.sources needs at least one source.";
            yield break;
        }

        foreach (var (source, index) in sources.Select((source, index) => (source, index)))
        {
            if (!ModSourceTypes.Contains(source.Type))
            {
                yield return $"mods.sources[{index}]: unknown type '{source.Type}' (known: {string.Join(", ", ModSourceTypes)}).";
            }
            else if (source.Type == "thunderstore" && string.IsNullOrWhiteSpace(source.Community))
            {
                yield return $"mods.sources[{index}]: thunderstore needs a community (e.g. valheim).";
            }
        }
    }

    private void Load(string root, bool builtIn, Dictionary<string, AdapterDefinition> found, List<AdapterProblem> problems)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var folder in Directory.GetDirectories(root))
        {
            var file = Path.Combine(folder, "adapter.json");
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var adapter = Read<AdapterDefinition>(folder, "adapter.json") ?? throw new JsonException("adapter.json is empty.");
                adapter = adapter with
                {
                    Folder = folder,
                    IsBuiltIn = builtIn,
                    Schema = Read<SettingsSchema>(folder, "settings.schema.json"),
                    Help = Read<AdapterHelp>(folder, "help.json"),
                };
                var errors = Validate(adapter, loaders: _loaderIds);
                if (errors.Count > 0)
                {
                    problems.Add(new AdapterProblem(folder, errors));
                    continue;
                }

                found[adapter.Id] = adapter;
            }
            catch (JsonException ex)
            {
                problems.Add(new AdapterProblem(folder, [ex.Message]));
            }
        }
    }

    /// <summary>One of the adapter's JSON files; null when it does not exist. An error names the file.</summary>
    private static T? Read<T>(string folder, string name)
        where T : class
    {
        var file = Path.Combine(folder, name);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), ContractJson.Options) ?? throw new JsonException("the file is empty.");
        }
        catch (JsonException ex)
        {
            throw new JsonException($"{name} is not valid JSON: {ex.Message}", ex);
        }
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex AdapterId();
}

public sealed record AdapterProblem(string Folder, IReadOnlyList<string> Errors);
