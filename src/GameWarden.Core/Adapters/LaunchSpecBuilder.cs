using System.Text.RegularExpressions;
using GameWarden.Contracts;
using GameWarden.Core.Instances;

namespace GameWarden.Core.Adapters;

/// <summary>
/// Renders an adapter's launch section for one instance (docs/04-adapter-spec.md §4). Arguments stay a list,
/// so a setting value can never add arguments; unknown placeholders are errors.
/// </summary>
public static partial class LaunchSpecBuilder
{
    public static LaunchSpec Build(
        AdapterDefinition adapter,
        string instanceId,
        string displayName,
        InstancePaths paths,
        IReadOnlyDictionary<string, int> ports,
        IReadOnlyDictionary<string, string> settings)
    {
        var launch = adapter.Launch ?? throw new InvalidOperationException($"Adapter {adapter.Id} has no launch section.");
        var values = TemplateValues(adapter, instanceId, displayName, paths, ports, settings);

        return new LaunchSpec
        {
            InstanceId = instanceId,
            Executable = Render(launch.Exe, values),
            Arguments = (launch.Args ?? []).Select(argument => Render(argument, values)).ToArray(),
            WorkingDirectory = Render(launch.WorkingDir ?? "{paths.server}", values),
            Environment = (launch.Env ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => Render(pair.Value, values)),
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
        IReadOnlyDictionary<string, string> settings)
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

        foreach (var (key, value) in settings)
        {
            values[$"settings.{key}"] = value;
        }

        return values;
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"Unknown placeholder {{{match.Groups[1].Value}}} in \"{template}\"."));

    /// <summary>Ports from adapter defaults and offsets (port allocation and conflicts come with the firewall work).</summary>
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
