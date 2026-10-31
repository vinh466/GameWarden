using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Adapters;

/// <summary>
/// <c>settings.schema.json</c> (L2), docs/04-adapter-spec.md §5: the settings the panel shows as a form, in groups,
/// and how each one reaches the game. Values are stored as text, like every setting.
/// </summary>
public sealed record SettingsSchema
{
    public IReadOnlyList<SettingGroup> Groups { get; init; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; init; } = [];

    public SettingDefinition? Find(string key) => Settings.FirstOrDefault(setting => setting.Key == key);
}

/// <summary>A section of the settings form; the order of the groups is the order of the sections.</summary>
public sealed record SettingGroup
{
    public string Id { get; init; } = string.Empty;

    [JsonConverter(typeof(LocalizedTextConverter))]
    public IReadOnlyDictionary<string, string>? Label { get; init; }

    [JsonConverter(typeof(LocalizedTextConverter))]
    public IReadOnlyDictionary<string, string>? Help { get; init; }

    /// <summary>Closed until opened, in the Settings tab.</summary>
    public bool Advanced { get; init; }
}

public sealed record SettingDefinition
{
    public static readonly string[] Types = ["string", "int", "bool", "enum", "number", "list", "secret"];

    public string Key { get; init; } = string.Empty;

    /// <summary><c>string</c>, <c>int</c>, <c>bool</c>, <c>enum</c>, <c>number</c>, <c>list</c> or <c>secret</c>.</summary>
    public string Type { get; init; } = string.Empty;

    public string Group { get; init; } = string.Empty;

    [JsonConverter(typeof(LocalizedTextConverter))]
    public IReadOnlyDictionary<string, string>? Label { get; init; }

    [JsonConverter(typeof(LocalizedTextConverter))]
    public IReadOnlyDictionary<string, string>? Help { get; init; }

    /// <summary>Shown with the field whatever its value, e.g. what changing it does to the world.</summary>
    [JsonConverter(typeof(LocalizedTextConverter))]
    public IReadOnlyDictionary<string, string>? Warning { get; init; }

    public JsonElement? Default { get; init; }

    /// <summary>Must be set before a start; a setting the launch section uses without a default is required too.</summary>
    public bool Required { get; init; }

    /// <summary>Asked in the new server wizard; required settings without a default always are.</summary>
    public bool Wizard { get; init; }

    /// <summary><c>int</c> and <c>number</c> bounds; a <c>number</c> may carry decimals.</summary>
    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    /// <summary>The step of a <c>number</c> input in the panel; <c>null</c> means the browser's default.</summary>
    public decimal? Step { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Pattern { get; init; }

    public IReadOnlyList<SettingOption> Options { get; init; } = [];

    public bool RestartRequired { get; init; } = true;

    /// <summary>A secret Core generates when an instance is created without one (ADR-0022 decision 5).</summary>
    public bool Generate { get; init; }

    /// <summary>
    /// A <c>list</c> of free text rather than of options: each item is written in quotes (<c>("A","B")</c>), for a
    /// game field such as Palworld's <c>DenyTechnologyList</c>.
    /// </summary>
    public bool Quoted { get; init; }

    public SettingTarget? Target { get; init; }

    /// <summary>The default as setting text (<c>true</c>, <c>600</c>, <c>normal</c>, <c>Steam,Xbox</c>); null when there is none.</summary>
    [JsonIgnore]
    public string? DefaultText => Default switch
    {
        null => null,
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        { ValueKind: JsonValueKind.String } text => text.GetString(),
        { ValueKind: JsonValueKind.Array } list => string.Join(",", list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText())),
        _ => null,
    };

    /// <summary>The values of a <c>list</c> setting in its stored text form: <c>Steam,Xbox</c>.</summary>
    public static IReadOnlyList<string> ListValues(string value) =>
        value.Length == 0
            ? []
            : [.. value.Split(',', StringSplitOptions.TrimEntries)];

    /// <summary>Why <paramref name="value"/> does not fit this setting; null when it does.</summary>
    public string? Check(string value)
    {
        if (Type == "bool")
        {
            return value is "true" or "false" ? null : $"Setting '{Key}' must be true or false.";
        }

        if (Type == "int")
        {
            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                return $"Setting '{Key}' must be a whole number.";
            }

            return OutOfRange(number, "a whole number");
        }

        if (Type == "number")
        {
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return $"Setting '{Key}' must be a number.";
            }

            return OutOfRange(number, "a number");
        }

        if (Type == "enum")
        {
            return Options.Any(option => option.Value == value)
                ? null
                : $"Setting '{Key}' must be one of: {string.Join(", ", Options.Select(option => option.Value))}.";
        }

        if (Type == "list")
        {
            var items = ListValues(value);
            if (items.FirstOrDefault(item => item.Length == 0) is not null)
            {
                return $"Setting '{Key}' has an empty list item.";
            }

            if (Options.Count > 0 && items.FirstOrDefault(item => Options.All(option => option.Value != item)) is { } unknown)
            {
                return $"Setting '{Key}' has '{unknown}', which is not one of: {string.Join(", ", Options.Select(option => option.Value))}.";
            }

            return FileProblem(value);
        }

        if (value.Length < (MinLength ?? 0))
        {
            return $"'{Key}' must be at least {MinLength} characters.";
        }

        if (value.Length > (MaxLength ?? SettingsValidation.MaxValueLength))
        {
            return $"'{Key}' must be at most {MaxLength ?? SettingsValidation.MaxValueLength} characters.";
        }

        if (Pattern is not null && !Regex.IsMatch(value, Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            return $"'{Key}' does not have the expected form.";
        }

        return FileProblem(value);
    }

    private string? OutOfRange(decimal number, string what) =>
        number < Min || number > Max
            ? $"Setting '{Key}' must be {what} from {Min?.ToString(CultureInfo.InvariantCulture) ?? "any"} to {Max?.ToString(CultureInfo.InvariantCulture) ?? "any"}."
            : null;

    /// <summary>A value written into a game config file may not carry a double quote or a line break (ADR-0022 decision 2).</summary>
    private string? FileProblem(string value) =>
        Target is { Kind: "file" } && (value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
            ? $"'{Key}' cannot contain a double quote or a line break: it goes into a game config file."
            : null;
}

public sealed record SettingOption
{
    public string Value { get; init; } = string.Empty;

    [JsonConverter(typeof(LocalizedTextConverter))]
    public IReadOnlyDictionary<string, string>? Label { get; init; }
}

/// <summary>
/// How the value reaches the game (04 §5.2). <c>arg</c>: <see cref="Args"/> are added after the launch arguments when
/// the value differs from the default (a <c>bool</c>: when it is true, and <see cref="OffArgs"/> when it is false).
/// <c>file</c>: Core writes the value into the field <see cref="Field"/> (default: the setting's key) of the declared
/// file <see cref="File"/> before every start (ADR-0022).
/// </summary>
public sealed record SettingTarget
{
    public string Kind { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>A <c>bool</c>'s args when it is false, for a game whose default is on (Valheim's <c>-public 0</c>).</summary>
    public IReadOnlyList<string> OffArgs { get; init; } = [];

    /// <summary>The <c>files[].name</c> of a <c>file</c> target.</summary>
    public string? File { get; init; }

    /// <summary>The field in the file; the setting's key when empty.</summary>
    public string? Field { get; init; }
}

/// <summary>A label in the panel's languages (docs/11 D1): a plain string is English, or <c>{ "en": …, "vi": … }</c>.</summary>
public sealed class LocalizedTextConverter : JsonConverter<IReadOnlyDictionary<string, string>>
{
    public override IReadOnlyDictionary<string, string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => new Dictionary<string, string> { ["en"] = reader.GetString()! },
            JsonTokenType.StartObject => JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options),
            _ => throw new JsonException("A label is a string or an object of language → text."),
        };

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
