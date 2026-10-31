using System.Globalization;
using System.Text.RegularExpressions;

namespace GameWarden.Core.Adapters;

/// <summary>One field of a <c>ue-struct-ini</c> file, as a setting writes it (ADR-0022 decision 1).</summary>
/// <param name="Quoted">For a <c>list</c>: write each item in quotes (<c>("A","B")</c>), as Palworld's technology IDs need.</param>
public sealed record FileFieldValue(string Field, string Type, string Value, bool Quoted = false);

/// <summary>
/// The first config-file format (ADR-0022 decision 1): an Unreal <c>Key=(A=1,B="x",C=(Y,Z))</c> line in an INI
/// section. Every field of the game's own template stays, in its order; fixed fields and settings replace theirs.
/// A field a newer game version added keeps the game's default, and a setting the template does not know is appended.
/// The output is only the section header and the line: Palworld resets a file with comments or extra lines.
/// </summary>
public static partial class UeStructIni
{
    /// <summary>The fields of the line, in file order.</summary>
    public static IReadOnlyList<(string Key, string Value)> Parse(string templateText, string section, string field)
    {
        return Fields(Line(templateText, section, field));
    }

    /// <summary>The template with <paramref name="fixedFields"/> and <paramref name="values"/> applied; everything else is kept as written.</summary>
    public static string Render(
        string templateText,
        string section,
        string field,
        IReadOnlyDictionary<string, string> fixedFields,
        IReadOnlyList<FileFieldValue> values)
    {
        var templateValue = Line(templateText, section, field);
        var fields = Fields(templateValue).ToList();
        foreach (var (key, value) in fixedFields)
        {
            Set(fields, key, value);
        }

        foreach (var value in values)
        {
            var existing = fields.FirstOrDefault(pair => pair.Key == value.Field).Value;
            Set(fields, value.Field, Format(value, existing));
        }

        var line = "(" + string.Join(",", fields.Select(pair => $"{pair.Key}={pair.Value}")) + ")";
        // Palworld reads only the section and the OptionSettings line; comments or extra lines make it reset the
        // file (the wiki: "must be 2 lines long"). The template is an input, not the output.
        return string.Concat("[", section, "]\r\n", field, "=", line, "\r\n");
    }

    /// <summary>How a setting's stored text is written; <paramref name="existing"/> is the template's raw value, for empty values.</summary>
    public static string Format(FileFieldValue value, string? existing = null)
    {
        var text = value.Value;
        if (value.Type == "bool")
        {
            return text switch
            {
                "true" or "True" => "True",
                "false" or "False" => "False",
                _ => throw new InvalidOperationException($"Field '{value.Field}': '{text}' is not true or false."),
            };
        }

        if (value.Type == "number")
        {
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString("0.000000", CultureInfo.InvariantCulture)
                : throw new InvalidOperationException($"Field '{value.Field}': '{text}' is not a number.");
        }

        if (value.Type == "int")
        {
            return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : throw new InvalidOperationException($"Field '{value.Field}': '{text}' is not a whole number.");
        }

        if (value.Type == "list")
        {
            var items = SettingDefinition.ListValues(text);
            if (items.Count == 0 && existing == string.Empty)
            {
                return string.Empty; // the template's own empty value stays empty
            }

            RejectUnsafe(value.Field, text);
            var rendered = value.Quoted ? items.Select(item => $"\"{item}\"") : items;
            return "(" + string.Join(",", rendered) + ")";
        }

        if (value.Type is "enum" or "string" or "secret")
        {
            RejectUnsafe(value.Field, text);
            if (value.Type != "enum" && text.Length == 0)
            {
                return existing == string.Empty ? string.Empty : "\"\"";
            }

            return value.Type == "enum" ? text : "\"" + text + "\"";
        }

        throw new InvalidOperationException($"Field '{value.Field}': type '{value.Type}' cannot be written to a config file.");
    }

    private static void Set(List<(string Key, string Value)> fields, string key, string value)
    {
        var index = fields.FindIndex(pair => pair.Key == key);
        if (index >= 0)
        {
            fields[index] = (key, value);
        }
        else
        {
            fields.Add((key, value));
        }
    }

    private static void RejectUnsafe(string field, string text)
    {
        if (text.Contains('"') || text.Contains('\r') || text.Contains('\n'))
        {
            throw new InvalidOperationException($"Field '{field}' cannot contain a double quote or a line break.");
        }
    }

    /// <summary>The template line's value; an error says what is missing.</summary>
    private static string Line(string templateText, string section, string field)
    {
        var header = SectionHeader().Matches(templateText)
            .FirstOrDefault(candidate => candidate.Groups["name"].Value.Trim() == $"[{section}]");
        if (header is null)
        {
            throw new InvalidOperationException($"The template has no [{section}] section.");
        }

        var start = header.Index + header.Length;
        var end = SectionHeader().Match(templateText, start);
        var body = end.Success ? templateText[start..end.Index] : templateText[start..];
        var line = new Regex($@"(?m)^[ \t]*{Regex.Escape(field)}[ \t]*=[ \t]*(?<value>[^\r\n]*)", RegexOptions.None).Match(body);
        if (!line.Success)
        {
            throw new InvalidOperationException($"The [{section}] section of the template has no {field}= line.");
        }

        return line.Groups["value"].Value;
    }

    private static List<(string Key, string Value)> Fields(string value)
    {
        var text = value.Trim();
        if (text.Length < 2 || text[0] != '(' || text[^1] != ')')
        {
            throw new InvalidOperationException("A ue-struct-ini value must be a parenthesised list: (A=1,B=2).");
        }

        var inner = text[1..^1];
        var fields = new List<(string Key, string Value)>();
        if (inner.Trim().Length == 0)
        {
            return fields;
        }

        foreach (var part in Split(inner))
        {
            var at = part.IndexOf('=');
            if (at <= 0)
            {
                throw new InvalidOperationException($"A ue-struct-ini field has no key=value form: '{part}'.");
            }

            fields.Add((part[..at].Trim(), part[(at + 1)..].Trim()));
        }

        return fields;
    }

    /// <summary>Splits on top-level commas; a quoted string or a nested list keeps its commas.</summary>
    private static IEnumerable<string> Split(string text)
    {
        var quoted = false;
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && quoted)
            {
                i++;
                continue;
            }

            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && ch == '(')
            {
                depth++;
            }
            else if (!quoted && ch == ')')
            {
                depth--;
            }
            else if (!quoted && depth == 0 && ch == ',')
            {
                yield return text[start..i].Trim();
                start = i + 1;
            }
        }

        yield return text[start..].Trim();
    }

    [GeneratedRegex(@"(?m)^[ \t]*(?<name>\[[^\]\r\n]+\])[ \t]*\r?$")]
    private static partial Regex SectionHeader();
}
