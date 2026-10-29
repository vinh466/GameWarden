using System.Globalization;

namespace GameWarden.Core.Adapters;

/// <summary>Checks <c>help.json</c> when an adapter loads (04 §5.3, §8); an invalid file makes the adapter invalid.</summary>
public static class AdapterHelpValidation
{
    /// <summary>
    /// Today where the calendar is furthest ahead (UTC+14), so a source checked today anywhere is never "in the future".
    /// </summary>
    public static DateOnly LatestToday(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime.AddHours(14));

    public static IEnumerable<string> Errors(AdapterDefinition adapter, DateOnly today)
    {
        if (adapter.Help is not { } help)
        {
            yield break;
        }

        if (adapter.Schema is not { } schema)
        {
            yield return "help.json needs settings.schema.json: its topics explain the settings form.";
            yield break;
        }

        if (help.Topics is null)
        {
            yield return "help.json: topics is required.";
            yield break;
        }

        foreach (var (key, topic) in help.Topics)
        {
            foreach (var error in TopicErrors(key, topic, schema, help, today))
            {
                yield return $"help.json: topic '{key}': {error}";
            }
        }
    }

    private static IEnumerable<string> TopicErrors(string key, HelpTopic? topic, SettingsSchema schema, AdapterHelp help, DateOnly today)
    {
        if (KeyError(key, schema) is { } keyError)
        {
            yield return keyError;
        }

        if (topic is null)
        {
            yield return "the topic is empty.";
            yield break;
        }

        if (topic.Title is not null && string.IsNullOrWhiteSpace(topic.Title))
        {
            yield return "title is empty; leave it out to use the label.";
        }

        if (string.IsNullOrWhiteSpace(topic.Summary))
        {
            yield return "summary is required.";
        }

        if (topic.Body is null || topic.Body.Count == 0)
        {
            yield return "body needs at least one block.";
        }
        else
        {
            for (var i = 0; i < topic.Body.Count; i++)
            {
                if (BlockError(topic.Body[i]) is { } blockError)
                {
                    yield return $"body[{i}] {blockError}";
                }
            }
        }

        foreach (var error in ValueErrors(key, topic.Values, schema))
        {
            yield return error;
        }

        if (topic.Tested is not null && string.IsNullOrWhiteSpace(topic.Tested))
        {
            yield return "tested is empty; leave it out when nothing was tested.";
        }

        if (topic.Sources is null || topic.Sources.Count == 0)
        {
            yield return "sources needs at least one source.";
        }
        else
        {
            for (var i = 0; i < topic.Sources.Count; i++)
            {
                foreach (var error in SourceErrors(topic.Sources[i], today))
                {
                    yield return $"sources[{i}] {error}";
                }
            }
        }

        foreach (var related in topic.Related ?? [])
        {
            if (!help.Has(related))
            {
                yield return $"related topic '{related}' does not exist in help.json.";
            }
            else if (related == key)
            {
                yield return "a topic is not related to itself.";
            }
        }
    }

    /// <summary>Why the topic key names nothing in the schema; null when it names a setting, a group or an option.</summary>
    private static string? KeyError(string key, SettingsSchema schema)
    {
        if (key.StartsWith(AdapterHelp.GroupPrefix, StringComparison.Ordinal))
        {
            var group = key[AdapterHelp.GroupPrefix.Length..];
            return schema.Groups.Any(candidate => candidate.Id == group) ? null : $"group '{group}' is not defined in settings.schema.json.";
        }

        if (key.StartsWith(AdapterHelp.OptionPrefix, StringComparison.Ordinal))
        {
            var parts = key[AdapterHelp.OptionPrefix.Length..].Split('=', 2);
            if (parts.Length != 2)
            {
                return "an option topic is keyed option:<setting key>=<value>.";
            }

            return schema.Find(parts[0]) switch
            {
                null => $"setting '{parts[0]}' is not defined in settings.schema.json.",
                { Type: not "enum" } => $"setting '{parts[0]}' is not an enum, so it has no options.",
                { } setting when setting.Options.All(option => option.Value != parts[1]) => $"'{parts[1]}' is not an option of '{parts[0]}'.",
                _ => null,
            };
        }

        return schema.Find(key) is null
            ? $"no setting '{key}' in settings.schema.json (a topic is keyed by a setting key, group:<id> or option:<key>=<value>)."
            : null;
    }

    private static string? BlockError(HelpBlock? block)
    {
        if (block is null || block.Kinds == 0)
        {
            return "needs one of p, list, tip or warning.";
        }

        if (block.Kinds > 1)
        {
            return "has more than one of p, list, tip and warning; use one block each.";
        }

        return block switch
        {
            { List: { } list } when list.Count == 0 => "list is empty.",
            { List: { } list } when list.Any(string.IsNullOrWhiteSpace) => "list has an empty item.",
            { List: null } when string.IsNullOrWhiteSpace(block.P ?? block.Tip ?? block.Warning) => "is empty.",
            _ => null,
        };
    }

    private static IEnumerable<string> ValueErrors(string key, IReadOnlyList<HelpValue>? values, SettingsSchema schema)
    {
        if (values is null)
        {
            yield break;
        }

        if (schema.Find(key) is not { Type: "enum" } setting)
        {
            yield return "values belong on an enum setting's own topic.";
            yield break;
        }

        if (values.Count == 0)
        {
            yield return "values is empty; leave it out instead.";
        }

        foreach (var value in values)
        {
            if (value is null || string.IsNullOrWhiteSpace(value.Value) || string.IsNullOrWhiteSpace(value.Text))
            {
                yield return "each value needs value and text.";
            }
            else if (setting.Options.All(option => option.Value != value.Value))
            {
                yield return $"value '{value.Value}' is not an option of '{key}'.";
            }
        }

        foreach (var twice in values.Where(value => value is not null).GroupBy(value => value.Value).Where(same => same.Count() > 1))
        {
            yield return $"value '{twice.Key}' is described twice.";
        }
    }

    private static IEnumerable<string> SourceErrors(HelpSource? source, DateOnly today)
    {
        if (source is null)
        {
            yield return "is empty.";
            yield break;
        }

        if (string.IsNullOrWhiteSpace(source.Title))
        {
            yield return "needs a title.";
        }

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(url.Host))
        {
            yield return $"url '{source.Url}' must be an absolute https URL.";
        }

        if (source.Publisher is not null && string.IsNullOrWhiteSpace(source.Publisher))
        {
            yield return "publisher is empty; leave it out instead.";
        }

        if (!DateOnly.TryParseExact(source.Checked, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkedOn))
        {
            yield return $"checked '{source.Checked}' must be a date as yyyy-MM-dd.";
        }
        else if (checkedOn > today)
        {
            yield return $"checked {source.Checked} is in the future.";
        }
    }
}
