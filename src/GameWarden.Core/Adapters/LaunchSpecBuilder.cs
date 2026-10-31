using System.Text.RegularExpressions;
using GameWarden.Contracts;
using GameWarden.Core.Instances;

namespace GameWarden.Core.Adapters;

/// <summary>
/// Renders an adapter's launch section for one instance (docs/04-adapter-spec.md §4). Arguments stay a list,
/// so a setting value can never add arguments; unknown placeholders are errors. Settings of an L2 adapter fall back
/// to their schema defaults, and their <c>arg</c> targets follow the launch arguments (04 §5.2). A mod loader's
/// arguments (docs/13 §4.3) come last, as given: they are not templates.
/// </summary>
public static partial class LaunchSpecBuilder
{
    public static LaunchSpec Build(
        AdapterDefinition adapter,
        string instanceId,
        string displayName,
        InstancePaths paths,
        IReadOnlyDictionary<string, int> ports,
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string>? secrets = null,
        IReadOnlyList<string>? loaderArguments = null)
    {
        var launch = adapter.Launch ?? throw new InvalidOperationException($"Adapter {adapter.Id} has no launch section.");
        var values = TemplateValues(adapter, instanceId, displayName, paths, ports, settings, secrets);

        return new LaunchSpec
        {
            InstanceId = instanceId,
            Executable = Render(launch.Exe, values),
            Arguments =
            [
                .. (launch.Args ?? []).Concat(TargetArgs(adapter, Effective(adapter, settings))).Select(argument => Render(argument, values)),
                .. loaderArguments ?? [],
            ],
            WorkingDirectory = Render(launch.WorkingDir ?? "{paths.server}", values),
            Environment = (launch.Env ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => Render(pair.Value, values)),
            ConsoleLogFile = launch.ConsoleLogFile is null ? null : Render(launch.ConsoleLogFile, values),
            LogDirectory = paths.Logs,
            StateDirectory = paths.RunnerState,
        };
    }

    public static IReadOnlyDictionary<string, string> TemplateValues(
        AdapterDefinition adapter,
        string instanceId,
        string displayName,
        InstancePaths paths,
        IReadOnlyDictionary<string, int> ports,
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string>? secrets = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["instance.id"] = instanceId,
            ["instance.displayName"] = displayName,
            ["paths.server"] = paths.Server,
            ["paths.config"] = paths.Config,
            ["paths.data"] = paths.Data,
            ["paths.mods"] = paths.Mods,
            ["paths.logs"] = paths.Logs,
            ["paths.adapter"] = adapter.Folder,
        };
        foreach (var (name, port) in ports)
        {
            values[$"ports.{name}"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var (key, value) in Effective(adapter, settings))
        {
            values[$"settings.{key}"] = value;
        }

        foreach (var (key, value) in secrets ?? new Dictionary<string, string>())
        {
            values[$"secrets.{key}"] = value;
        }

        return values;
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"Unknown placeholder {{{match.Groups[1].Value}}} in \"{template}\"."));

    /// <summary>The placeholder names in a template, without braces.</summary>
    public static IEnumerable<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(match => match.Groups[1].Value);

    /// <summary>The stored settings over the schema defaults; an empty value falls back to the default when there is one.</summary>
    public static IReadOnlyDictionary<string, string> Effective(AdapterDefinition adapter, IReadOnlyDictionary<string, string> settings)
    {
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in adapter.Schema?.Settings ?? [])
        {
            if (setting.DefaultText is { } text)
            {
                effective[setting.Key] = text;
            }
        }

        foreach (var (key, value) in settings)
        {
            if (value.Length > 0 || !effective.ContainsKey(key))
            {
                effective[key] = value;
            }
        }

        return effective;
    }

    /// <summary>
    /// The args of each <c>arg</c> target in schema order: when the value differs from the default, or for a
    /// <c>bool</c> its args when it is true and its off args when it is false. What the game always needs is in the
    /// launch section instead (04 §5.2).
    /// </summary>
    public static IEnumerable<string> TargetArgs(AdapterDefinition adapter, IReadOnlyDictionary<string, string> effective)
    {
        foreach (var setting in adapter.Schema?.Settings ?? [])
        {
            if (setting.Target is not { Kind: "arg" } target || !effective.TryGetValue(setting.Key, out var value))
            {
                continue;
            }

            var arguments = setting.Type == "bool"
                ? value == "true" ? target.Args : value == "false" ? target.OffArgs : []
                : value.Length > 0 && value != setting.DefaultText ? target.Args : [];
            foreach (var argument in arguments)
            {
                yield return argument;
            }
        }
    }

    /// <summary>
    /// What an instance must have before it can start: the settings and secrets the launch section uses, less the
    /// settings with a schema default, plus the schema's required ones.
    /// </summary>
    public static RequiredValues Required(AdapterDefinition adapter)
    {
        var used = TemplateNames(adapter);
        var schema = adapter.Schema?.Settings ?? [];
        return new RequiredValues(
            [.. used.Settings
                .Where(key => adapter.Schema?.Find(key)?.DefaultText is null)
                .Union(schema.Where(setting => setting is { Required: true, DefaultText: null } && setting.Type != "secret").Select(setting => setting.Key))
                .Union(schema.Where(setting => setting.Target is { Kind: "file" } && setting.DefaultText is null).Select(setting => setting.Key))
                .Order(StringComparer.Ordinal)],
            [.. used.Secrets
                .Union(schema.Where(setting => setting is { Required: true, Type: "secret" }).Select(setting => setting.Key))
                .Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Settings and secrets the launch, readiness and stop templates use. A secret used only in a declared file's
    /// fixed fields is not here: it may be optional, and a missing optional secret is written as an empty value.
    /// </summary>
    public static RequiredValues TemplateNames(AdapterDefinition adapter)
    {
        var launch = adapter.Launch;
        var readiness = adapter.Readiness;
        var stop = adapter.Stop;
        IEnumerable<string?> templates =
        [
            launch?.Exe,
            launch?.WorkingDir,
            launch?.ConsoleLogFile,
            readiness?.Source,
            readiness?.Url,
            readiness?.Auth?.Username,
            readiness?.Auth?.Password,
            stop?.Command,
            stop?.Auth?.Username,
            stop?.Auth?.Password,
            .. launch?.Args ?? [],
            .. launch?.Env?.Values ?? [],
            .. stop?.Requests?.SelectMany(request => new string?[] { request.Url, request.Body }) ?? [],
        ];
        return Names(templates);
    }

    /// <summary><see cref="TemplateNames"/> plus the fixed fields of every declared file, for schema validation.</summary>
    public static RequiredValues DeclaredTemplateNames(AdapterDefinition adapter)
    {
        var names = TemplateNames(adapter);
        var fixedNames = Names(adapter.Files.SelectMany(file => (file.Fixed?.Values ?? Enumerable.Empty<string>()).Cast<string?>()));
        return new RequiredValues(
            [.. names.Settings.Union(fixedNames.Settings).Order(StringComparer.Ordinal)],
            [.. names.Secrets.Union(fixedNames.Secrets).Order(StringComparer.Ordinal)]);
    }

    private static RequiredValues Names(IEnumerable<string?> templates)
    {
        var names = templates.OfType<string>().SelectMany(template => Placeholder().Matches(template)).Select(match => match.Groups[1].Value).ToArray();
        return new RequiredValues(
            [.. names.Where(name => name.StartsWith("settings.", StringComparison.Ordinal)).Select(name => name["settings.".Length..]).Distinct().Order(StringComparer.Ordinal)],
            [.. names.Where(name => name.StartsWith("secrets.", StringComparison.Ordinal)).Select(name => name["secrets.".Length..]).Distinct().Order(StringComparer.Ordinal)]);
    }

    /// <summary>Ports from adapter defaults and offsets; <see cref="Network.Ports.Allocate"/> moves them for further instances.</summary>
    public static IReadOnlyDictionary<string, int> DefaultPorts(AdapterDefinition adapter)
    {
        var ports = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var port in adapter.Ports.Where(port => port.Default is not null))
        {
            ports[port.Name] = port.Default!.Value;
        }

        foreach (var port in adapter.Ports.Where(port => port.Offset is not null))
        {
            ports[port.Name] = ports[port.Offset!.Of] + port.Offset.By;
        }

        return ports;
    }

    [GeneratedRegex(@"\{([a-zA-Z]+\.[a-zA-Z0-9_-]+)\}")]
    private static partial Regex Placeholder();
}

public sealed record RequiredValues(IReadOnlyList<string> Settings, IReadOnlyList<string> Secrets)
{
    /// <summary>A message that says what is missing and the CLI commands that fix it; null when nothing is.</summary>
    public string? DescribeMissing(string instanceId, IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> secretsSet)
    {
        var missingSettings = Settings.Where(name => !settings.TryGetValue(name, out var value) || string.IsNullOrEmpty(value)).ToArray();
        var missingSecrets = Secrets.Where(name => !secretsSet.Contains(name)).ToArray();
        var parts = new List<string>();
        if (missingSettings.Length > 0)
        {
            parts.Add($"Missing settings: {string.Join(", ", missingSettings)} (gamewarden set {instanceId} {string.Join(" ", missingSettings.Select(name => $"{name}=..."))}).");
        }

        foreach (var secret in missingSecrets)
        {
            parts.Add($"Missing secret: {secret} (gamewarden secret {instanceId} {secret}).");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
