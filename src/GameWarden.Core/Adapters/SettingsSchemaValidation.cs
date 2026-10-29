using System.Text.RegularExpressions;

namespace GameWarden.Core.Adapters;

/// <summary>Checks <c>settings.schema.json</c> when an adapter loads (04 §8), and values against it on create and save.</summary>
public static partial class SettingsSchemaValidation
{
    public static IEnumerable<string> Errors(AdapterDefinition adapter)
    {
        var schema = adapter.Schema ?? new SettingsSchema();
        var groups = schema.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in schema.Groups.GroupBy(group => group.Id).Where(same => same.Count() > 1).Select(same => same.Key))
        {
            yield return $"settings.schema.json: group '{id}' is defined twice.";
        }

        foreach (var group in schema.Groups.Where(group => !GroupId().IsMatch(group.Id)))
        {
            yield return $"settings.schema.json: group id '{group.Id}' must be letters, digits or '-'.";
        }

        foreach (var key in schema.Settings.GroupBy(setting => setting.Key).Where(same => same.Count() > 1).Select(same => same.Key))
        {
            yield return $"settings.schema.json: setting '{key}' is defined twice.";
        }

        foreach (var error in schema.Settings.SelectMany(setting => SettingErrors(setting, groups)))
        {
            yield return error;
        }

        // Every value the launch section uses is described, so the panel can show it.
        var used = LaunchSpecBuilder.TemplateNames(adapter);
        foreach (var key in used.Settings.Where(key => schema.Find(key) is null or { Type: "secret" }))
        {
            yield return $"settings.schema.json does not define the setting '{key}' that the launch section uses.";
        }

        foreach (var key in used.Secrets.Where(key => schema.Find(key) is not { Type: "secret" }))
        {
            yield return $"settings.schema.json does not define the secret '{key}' that the launch section uses.";
        }
    }

    /// <summary>
    /// The first value that does not fit its setting; null when all fit. Empty values (back to the default) and keys
    /// the schema does not know pass, as before (04 §4). <paramref name="secrets"/> checks secret values instead.
    /// </summary>
    public static string? ValueProblem(SettingsSchema? schema, IEnumerable<KeyValuePair<string, string?>> values, bool secrets = false)
    {
        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrEmpty(value)
                && schema?.Find(key) is { } setting
                && (setting.Type == "secret") == secrets
                && setting.Check(value) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private static IEnumerable<string> SettingErrors(SettingDefinition setting, HashSet<string> groups)
    {
        var at = $"settings.schema.json: setting '{setting.Key}'";
        if (!SettingKey().IsMatch(setting.Key))
        {
            yield return $"{at}: the key must be 1-64 letters, digits, '_' or '-'.";
        }

        if (!SettingDefinition.Types.Contains(setting.Type))
        {
            yield return $"{at}: type must be {string.Join(", ", SettingDefinition.Types)}.";
            yield break;
        }

        if (!groups.Contains(setting.Group))
        {
            yield return $"{at}: group '{setting.Group}' is not defined.";
        }

        if (setting.Type == "enum" && setting.Options.Count == 0)
        {
            yield return $"{at}: an enum needs options.";
        }

        if (setting.Pattern is not null && !IsRegex(setting.Pattern))
        {
            yield return $"{at}: pattern is not a regular expression.";
            yield break;
        }

        if (setting.Type == "secret")
        {
            if (setting.Default is not null || setting.Target is not null)
            {
                yield return $"{at}: a secret has no default and no target; the launch section uses it as {{secrets.{setting.Key}}}.";
            }

            yield break;
        }

        if (setting.Default is not null && (setting.DefaultText is not { } text || setting.Check(text) is not null))
        {
            yield return $"{at}: the default does not fit the setting.";
        }

        if (setting.Target is { } target)
        {
            if (target.Kind != "arg")
            {
                yield return $"{at}: target kind '{target.Kind}' is not supported yet (only arg).";
            }
            else if (target.Args.Count == 0)
            {
                yield return $"{at}: an arg target needs args.";
            }
            else if (target.Args.Concat(target.OffArgs).Any(arg => arg.Contains("{secrets.", StringComparison.Ordinal)))
            {
                yield return $"{at}: secrets reach the game through the launch section only.";
            }

            if (target.OffArgs.Count > 0 && setting.Type != "bool")
            {
                yield return $"{at}: offArgs is for bool settings only.";
            }
        }
    }

    private static bool IsRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SettingKey();

    [GeneratedRegex("^[a-z][a-zA-Z0-9-]{0,63}$")]
    private static partial Regex GroupId();
}
