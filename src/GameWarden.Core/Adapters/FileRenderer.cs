using System.Text;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;

namespace GameWarden.Core.Adapters;

/// <summary>
/// Renders an adapter's declared config files before every start as <c>gamewarden-run</c> (ADR-0022 decision 1):
/// the game's own template, then its fixed fields, then every setting that targets the file. The result goes to the
/// game's folder and a copy stays in <c>{paths.config}</c>. A file that no setting names keeps the template's value,
/// so a field a newer game version added keeps the game's default.
/// </summary>
public static class FileRenderer
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The first value that cannot be written into a declared file (a quote or a line break in a secret used by a
    /// fixed field); null when all fit. Optional secrets may be absent and are written as an empty value.
    /// </summary>
    public static string? Problem(
        AdapterDefinition adapter,
        IReadOnlyDictionary<string, string> secrets)
    {
        foreach (var file in adapter.Files)
        {
            foreach (var value in file.Fixed?.Values ?? Enumerable.Empty<string>())
            {
                foreach (var name in LaunchSpecBuilder.Placeholders(value).Where(name => name.StartsWith("secrets.", StringComparison.Ordinal)))
                {
                    var secret = name["secrets.".Length..];
                    if (secrets.TryGetValue(secret, out var text) && (text.Contains('"') || text.Contains('\r') || text.Contains('\n')))
                    {
                        return $"Secret '{secret}' cannot contain a double quote or a line break: it goes into {file.Name}.";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Renders every declared file; returns the paths written, for the log.</summary>
    public static IReadOnlyList<string> Render(
        AdapterDefinition adapter,
        InstancePaths paths,
        string instanceId,
        string displayName,
        IReadOnlyDictionary<string, int> ports,
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string> secrets,
        IInstanceFiles files)
    {
        if (adapter.Files.Count == 0)
        {
            return [];
        }

        if (Problem(adapter, secrets) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var values = LaunchSpecBuilder.TemplateValues(adapter, instanceId, displayName, paths, ports, settings, WithEmptySecrets(adapter, secrets));
        var written = new List<string>();
        foreach (var file in adapter.Files)
        {
            var target = LaunchSpecBuilder.Render(file.Path, values);
            var template = LaunchSpecBuilder.Render(file.Template, values);
            var templateText = files.Run(() => File.Exists(template) ? File.ReadAllText(template) : null)
                ?? throw new InvalidOperationException($"The game's template file was not found: {template}. Install the game first.");

            var fixedFields = (file.Fixed ?? new Dictionary<string, string>())
                .ToDictionary(pair => pair.Key, pair => LaunchSpecBuilder.Render(pair.Value, values), StringComparer.Ordinal);
            var settingValues = SettingValues(adapter, file.Name, settings);
            var rendered = file.Format switch
            {
                "ue-struct-ini" => UeStructIni.Render(templateText, file.Section!, file.Field!, fixedFields, settingValues),
                _ => throw new InvalidOperationException($"File '{file.Name}': unknown format '{file.Format}'."),
            };

            var copy = Path.Combine(paths.Config, Path.GetFileName(target));
            files.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, rendered, Utf8);
                Directory.CreateDirectory(paths.Config);
                File.WriteAllText(copy, rendered, Utf8);
            });
            written.Add(target);
        }

        return written;
    }

    private static IReadOnlyList<FileFieldValue> SettingValues(
        AdapterDefinition adapter,
        string fileName,
        IReadOnlyDictionary<string, string> settings)
    {
        var effective = LaunchSpecBuilder.Effective(adapter, settings);
        var values = new List<FileFieldValue>();
        foreach (var setting in adapter.Schema?.Settings ?? [])
        {
            if (setting.Target is not { Kind: "file" } target || target.File != fileName)
            {
                continue;
            }

            if (!effective.TryGetValue(setting.Key, out var value))
            {
                throw new InvalidOperationException($"Setting '{setting.Key}' has no value, and {fileName} needs it.");
            }

            values.Add(new FileFieldValue(target.Field ?? setting.Key, setting.Type, value, setting.Quoted));
        }

        return values;
    }

    /// <summary>A missing optional secret becomes an empty value, so a fixed field such as ServerPassword can be unset.</summary>
    private static IReadOnlyDictionary<string, string> WithEmptySecrets(
        AdapterDefinition adapter,
        IReadOnlyDictionary<string, string> secrets)
    {
        var values = new Dictionary<string, string>(secrets, StringComparer.Ordinal);
        foreach (var name in LaunchSpecBuilder.DeclaredTemplateNames(adapter).Secrets)
        {
            values.TryAdd(name, string.Empty);
        }

        return values;
    }
}
