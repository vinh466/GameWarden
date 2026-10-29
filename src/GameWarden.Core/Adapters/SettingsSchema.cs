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
    public static readonly string[] Types = ["string", "int", "bool", "enum", "secret"];

    public string Key { get; init; } = string.Empty;

    /// <summary><c>string</c>, <c>int</c>, <c>bool</c>, <c>enum</c> or <c>secret</c>.</summary>
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

    public int? Min { get; init; }

    public int? Max { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Pattern { get; init; }

    public IReadOnlyList<SettingOption> Options { get; init; } = [];

    public bool RestartRequired { get; init; } = true;

    public SettingTarget? Target { get; init; }

    /// <summary>The default as setting text (<c>true</c>, <c>600</c>, <c>normal</c>); null when there is none.</summary>
    [JsonIgnore]
    public string? DefaultText => Default switch
    {
        null => null,
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        { ValueKind: JsonValueKind.String } text => text.GetString(),
        _ => null,
    };

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

            return number < Min || number > Max
                ? $"Setting '{Key}' must be from {Min?.ToString(CultureInfo.InvariantCulture) ?? "any"} to {Max?.ToString(CultureInfo.InvariantCulture) ?? "any"}."
                : null;
        }

        if (Type == "enum")
        {
            return Options.Any(option => option.Value == value)
                ? null
                : $"Setting '{Key}' must be one of: {string.Join(", ", Options.Select(option => option.Value))}.";
        }

        if (value.Length < (MinLength ?? 0))
        {
            return $"'{Key}' must be at least {MinLength} characters.";
        }

        if (value.Length > (MaxLength ?? SettingsValidation.MaxValueLength))
        {
            return $"'{Key}' must be at most {MaxLength ?? SettingsValidation.MaxValueLength} characters.";
        }

        return Pattern is not null && !Regex.IsMatch(value, Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            ? $"'{Key}' does not have the expected form."
            : null;
    }
}

public sealed record SettingOption
{
    public string Value { get; init; } = string.Empty;

    [JsonConverter(typeof(LocalizedTextConverter))]
    public IReadOnlyDictionary<string, string>? Label { get; init; }
}

/// <summary>
/// How the value reaches the game (04 §5.2). Only <c>arg</c> exists so far: <see cref="Args"/> are added after the
/// launch arguments when the value differs from the default (a <c>bool</c>: when it is true, and
/// <see cref="OffArgs"/> when it is false).
/// </summary>
public sealed record SettingTarget
{
    public string Kind { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>A <c>bool</c>'s args when it is false, for a game whose default is on (Valheim's <c>-public 0</c>).</summary>
    public IReadOnlyList<string> OffArgs { get; init; } = [];
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
