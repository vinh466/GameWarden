using System.Text.Json.Serialization;

namespace GameWarden.Core.Adapters;

/// <summary>
/// <c>help.json</c> next to <c>settings.schema.json</c> (04 §5.3, docs/12 §5): the longer explanations the panel opens in
/// its help drawer, with their sources. Topics are keyed by a setting key, <c>group:&lt;id&gt;</c> or
/// <c>option:&lt;key&gt;=&lt;value&gt;</c>; the text is English (docs/12 §3). The same records are the file and the
/// answer of <c>GET /adapters/{id}/help</c>, where a missing field is <c>null</c>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdapterHelp(IReadOnlyDictionary<string, HelpTopic> Topics)
{
    public const string GroupPrefix = "group:";

    public const string OptionPrefix = "option:";

    public static AdapterHelp Empty { get; } = new(new Dictionary<string, HelpTopic>());

    public bool Has(string key) => Topics?.ContainsKey(key) == true;
}

/// <param name="Title">Null: the panel uses the setting's, group's or option's label.</param>
/// <param name="Summary">One or two sentences: the short answer.</param>
/// <param name="Values">What each option of an enum setting means; only on a setting's own topic.</param>
/// <param name="Tested">What a VM walk-through or a test showed, with its date.</param>
/// <param name="Verified">False while any statement rests on an unverified source: the panel shows "Not verified yet".</param>
/// <param name="Sources">At least one; the panel lists them last.</param>
/// <param name="Related">Other topic keys of the same file, opened in the same drawer.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HelpTopic(
    string? Title,
    string Summary,
    IReadOnlyList<HelpBlock> Body,
    IReadOnlyList<HelpValue>? Values,
    string? Tested,
    [property: JsonRequired] bool Verified,
    IReadOnlyList<HelpSource> Sources,
    IReadOnlyList<string>? Related);

/// <summary>Exactly one of a paragraph, a list, a tip or a warning; plain text, never markup (docs/11 §10.7).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HelpBlock(string? P, IReadOnlyList<string>? List, string? Tip, string? Warning)
{
    /// <summary>How many of the four kinds the block sets; a valid block sets one.</summary>
    [JsonIgnore]
    public int Kinds => (P is null ? 0 : 1) + (List is null ? 0 : 1) + (Tip is null ? 0 : 1) + (Warning is null ? 0 : 1);
}

/// <summary>One option of an enum setting and what it means; the panel marks the default from the schema.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HelpValue(string Value, string Text);

/// <param name="Url">An absolute <c>https</c> URL.</param>
/// <param name="Checked">The day the source was last checked, <c>yyyy-MM-dd</c>.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HelpSource(string Title, string Url, string? Publisher, string Checked);
