using System.Text.Json;
using System.Text.RegularExpressions;
using GameWarden.Contracts;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Adapters;

/// <summary>
/// Loads adapters: built-in ones read-only from the install folder, the Owner's from the data root,
/// where the same <c>id</c> overrides a built-in adapter (OQ-18). Invalid adapters are listed with
/// their errors and cannot be used (docs/04-adapter-spec.md §8).
/// </summary>
public sealed partial class AdapterCatalog(IOptions<GameWardenOptions> options, ILogger<AdapterCatalog> logger)
{
    private static readonly string[] Levels = ["L0", "L1", "L2", "L3"];
    private static readonly string[] StopMethods = ["ctrl-c", "stdin", "rcon", "rest", "kill"];
    private static readonly string[] ReadinessTypes = ["logRegex", "delay", "port"];

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

    public static IReadOnlyList<string> Validate(AdapterDefinition adapter)
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
                var adapter = JsonSerializer.Deserialize<AdapterDefinition>(File.ReadAllText(file), ContractJson.Options)
                    ?? throw new JsonException("empty file");
                adapter = adapter with { Folder = folder, IsBuiltIn = builtIn };
                var errors = Validate(adapter);
                if (errors.Count > 0)
                {
                    problems.Add(new AdapterProblem(folder, errors));
                    continue;
                }

                found[adapter.Id] = adapter;
            }
            catch (JsonException ex)
            {
                problems.Add(new AdapterProblem(folder, [$"adapter.json is not valid JSON: {ex.Message}"]));
            }
        }
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex AdapterId();
}

public sealed record AdapterProblem(string Folder, IReadOnlyList<string> Errors);
