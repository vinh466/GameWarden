using System.Text.Json;
using System.Text.Json.Nodes;
using GameWarden.Contracts;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GameWarden.Core.Tests.Unit;

/// <summary><c>help.json</c> (04 §5.3, docs/12 §5): the rules Core checks when an adapter loads, and the hasHelp flags.</summary>
[Trait("Category", "Unit")]
public sealed class AdapterHelpTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    private const string Schema = """
        {
          "groups": [{ "id": "server", "label": "Server" }, { "id": "world", "label": "World" }],
          "settings": [
            { "key": "name", "type": "string", "group": "server", "required": true },
            { "key": "crossplay", "type": "bool", "group": "server", "default": false, "target": { "kind": "arg", "args": ["-crossplay"] } },
            {
              "key": "combat", "type": "enum", "group": "world", "default": "normal",
              "options": [{ "value": "easy" }, { "value": "normal" }, { "value": "hard" }],
              "target": { "kind": "arg", "args": ["-modifier", "combat", "{settings.combat}"] }
            }
          ]
        }
        """;

    /// <summary>A setting topic with every field, a group topic and an option topic.</summary>
    private const string Help = """
        {
          "topics": {
            "combat": {
              "title": "Combat",
              "summary": "How hard fights are.",
              "body": [{ "p": "A paragraph." }, { "list": ["One", "Two"] }, { "tip": "A tip." }, { "warning": "A warning." }],
              "values": [{ "value": "easy", "text": "Easier." }, { "value": "hard", "text": "Harder." }],
              "tested": "Checked on the VM, 2026-10-29.",
              "verified": true,
              "sources": [{ "title": "Guide", "url": "https://example.com/guide", "publisher": "Example", "checked": "2026-10-29" }],
              "related": ["group:world", "option:combat=hard"]
            },
            "group:world": {
              "summary": "The world.",
              "body": [{ "p": "About the world." }],
              "verified": false,
              "sources": [{ "title": "Wiki", "url": "https://wiki.example.org/World", "checked": "2026-10-01" }]
            },
            "option:combat=hard": {
              "summary": "Hard combat.",
              "body": [{ "p": "More damage." }],
              "verified": true,
              "sources": [{ "title": "Guide", "url": "https://example.com/guide", "checked": "2026-10-29" }]
            }
          }
        }
        """;

    private static AdapterDefinition Adapter(string? help = Help) => new()
    {
        SchemaVersion = 1,
        Id = "demo",
        Name = "Demo",
        Version = "1.0.0",
        Level = "L2",
        Source = new AdapterSource("manual"),
        Launch = new AdapterLaunch("{paths.server}\\demo.exe", ["-name", "{settings.name}"]),
        Readiness = new AdapterReadiness("logRegex", "stdout", "ready"),
        Stop = new AdapterStop("ctrl-c"),
        Backup = new AdapterBackup(["{paths.data}"], "stop"),
        Resources = new AdapterResources(new ResourceAmounts(1, 512, 100)),
        Schema = JsonSerializer.Deserialize<SettingsSchema>(Schema, ContractJson.Options),
        Help = help is null ? null : JsonSerializer.Deserialize<AdapterHelp>(help, ContractJson.Options),
        Folder = @"C:\adapters\demo",
    };

    /// <summary>The errors after <paramref name="change"/> edits the valid help file.</summary>
    private static IReadOnlyList<string> ErrorsAfter(Action<JsonObject> change)
    {
        var help = JsonNode.Parse(Help)!.AsObject();
        change(help["topics"]!.AsObject());
        return AdapterCatalog.Validate(Adapter(help.ToJsonString()), Today);
    }

    private static JsonObject Topic(JsonObject topics, string key) => topics[key]!.AsObject();

    [Fact]
    public void A_complete_help_file_has_no_errors()
    {
        Assert.Empty(AdapterCatalog.Validate(Adapter(), Today));
        Assert.Empty(AdapterCatalog.Validate(Adapter(help: null), Today));
    }

    [Theory]
    [InlineData("nameless", "no setting 'nameless' in settings.schema.json")]
    [InlineData("group:saves", "group 'saves' is not defined")]
    [InlineData("option:combat=brutal", "'brutal' is not an option of 'combat'")]
    [InlineData("option:crossplay=true", "'crossplay' is not an enum")]
    [InlineData("option:missing=x", "setting 'missing' is not defined")]
    [InlineData("option:combat", "keyed option:<setting key>=<value>")]
    public void Topic_keys_must_name_a_setting_a_group_or_an_option(string key, string expected)
    {
        var errors = ErrorsAfter(topics => topics[key] = Topic(topics, "group:world").DeepClone());

        Assert.Contains(errors, error => error.StartsWith($"help.json: topic '{key}': ", StringComparison.Ordinal) && error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Setting_group_and_option_keys_are_accepted()
    {
        var errors = ErrorsAfter(topics =>
        {
            topics["name"] = Topic(topics, "group:world").DeepClone();
            topics["group:server"] = Topic(topics, "group:world").DeepClone();
            topics["option:combat=easy"] = Topic(topics, "group:world").DeepClone();
        });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("""{ "summary": "" }""", "summary is required.")]
    [InlineData("""{ "summary": null }""", "summary is required.")]
    [InlineData("""{ "title": " " }""", "title is empty")]
    [InlineData("""{ "tested": "" }""", "tested is empty")]
    [InlineData("""{ "body": [] }""", "body needs at least one block.")]
    [InlineData("""{ "body": null }""", "body needs at least one block.")]
    [InlineData("""{ "body": [{}] }""", "body[0] needs one of p, list, tip or warning.")]
    [InlineData("""{ "body": [{ "p": "x", "tip": "y" }] }""", "body[0] has more than one of p, list, tip and warning")]
    [InlineData("""{ "body": [{ "p": "x" }, { "list": [] }] }""", "body[1] list is empty.")]
    [InlineData("""{ "body": [{ "list": ["a", " "] }] }""", "body[0] list has an empty item.")]
    [InlineData("""{ "body": [{ "warning": "" }] }""", "body[0] is empty.")]
    [InlineData("""{ "values": [] }""", "values is empty")]
    [InlineData("""{ "values": [{ "value": "brutal", "text": "No." }] }""", "value 'brutal' is not an option of 'combat'.")]
    [InlineData("""{ "values": [{ "value": "easy", "text": "" }] }""", "each value needs value and text.")]
    [InlineData("""{ "values": [{ "value": "easy", "text": "A" }, { "value": "easy", "text": "B" }] }""", "value 'easy' is described twice.")]
    [InlineData("""{ "sources": [] }""", "sources needs at least one source.")]
    [InlineData("""{ "sources": null }""", "sources needs at least one source.")]
    [InlineData("""{ "related": ["option:combat=easy"] }""", "related topic 'option:combat=easy' does not exist in help.json.")]
    [InlineData("""{ "related": ["combat"] }""", "a topic is not related to itself.")]
    public void Topic_content_mistakes_are_reported(string patch, string expected)
    {
        var errors = ErrorsAfter(topics => Patch(Topic(topics, "combat"), patch));

        Assert.Contains(errors, error => error.StartsWith($"help.json: topic 'combat': {expected}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://example.com/guide", "must be an absolute https URL")]
    [InlineData("/docs/guide", "must be an absolute https URL")]
    [InlineData("ftp://example.com/guide", "must be an absolute https URL")]
    [InlineData("javascript:alert(1)", "must be an absolute https URL")]
    [InlineData("", "must be an absolute https URL")]
    public void Source_urls_must_be_absolute_https(string url, string expected)
    {
        var errors = ErrorsAfter(topics => Topic(topics, "combat")["sources"]![0]!["url"] = url);

        Assert.Contains(errors, error => error.Contains("sources[0] url", StringComparison.Ordinal) && error.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("29.10.2026", "must be a date as yyyy-MM-dd")]
    [InlineData("2026-10-29T10:00", "must be a date as yyyy-MM-dd")]
    [InlineData("2026-13-01", "must be a date as yyyy-MM-dd")]
    [InlineData("2026-10-30", "is in the future")]
    public void Checked_dates_are_iso_days_not_in_the_future(string date, string expected)
    {
        var errors = ErrorsAfter(topics => Topic(topics, "combat")["sources"]![0]!["checked"] = date);

        Assert.Contains(errors, error => error.Contains("sources[0] checked", StringComparison.Ordinal) && error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Sources_need_a_title_and_a_publisher_is_never_blank()
    {
        var errors = ErrorsAfter(topics =>
        {
            Topic(topics, "combat")["sources"]![0]!["title"] = "";
            Topic(topics, "combat")["sources"]![0]!["publisher"] = " ";
        });

        Assert.Contains("help.json: topic 'combat': sources[0] needs a title.", errors);
        Assert.Contains(errors, error => error.Contains("publisher is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void Values_belong_on_an_enum_settings_own_topic()
    {
        var values = """[{ "value": "hard", "text": "Harder." }]""";

        var onGroup = ErrorsAfter(topics => Topic(topics, "group:world")["values"] = JsonNode.Parse(values));
        var onOption = ErrorsAfter(topics => Topic(topics, "option:combat=hard")["values"] = JsonNode.Parse(values));
        var onBool = ErrorsAfter(topics => topics["crossplay"] = Patched(Topic(topics, "group:world"), $$"""{ "values": {{values}} }"""));

        Assert.Contains("help.json: topic 'group:world': values belong on an enum setting's own topic.", onGroup);
        Assert.Contains("help.json: topic 'option:combat=hard': values belong on an enum setting's own topic.", onOption);
        Assert.Contains("help.json: topic 'crossplay': values belong on an enum setting's own topic.", onBool);
    }

    [Fact]
    public void Help_needs_a_settings_schema()
    {
        var errors = AdapterCatalog.Validate(Adapter() with { Level = "L1", Schema = null }, Today);

        Assert.Contains("help.json needs settings.schema.json: its topics explain the settings form.", errors);
    }

    [Fact]
    public void A_help_file_without_topics_is_reported()
    {
        Assert.Contains("help.json: topics is required.", AdapterCatalog.Validate(Adapter("{}"), Today));
    }

    [Fact]
    public void Today_is_taken_where_the_calendar_is_furthest_ahead()
    {
        // 10:30 UTC on 2026-10-29 is already 2026-10-30 in UTC+14, so a source checked "today" anywhere passes.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 10, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 30), AdapterHelpValidation.LatestToday(time));
    }

    [Fact]
    public void Loading_refuses_unknown_fields_a_missing_verified_flag_and_bad_json_naming_the_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "help-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            var valid = WriteAdapter(root, "valid", Help);
            var misspelt = WriteAdapter(root, "misspelt", Help.Replace("\"tested\"", "\"testd\"", StringComparison.Ordinal));
            var unverified = WriteAdapter(root, "unverified", Help.Replace("\"verified\": false,", "", StringComparison.Ordinal));
            var broken = WriteAdapter(root, "broken", "{ \"topics\": ");
            var catalog = new AdapterCatalog(
                Options.Create(new GameWardenOptions { DataRoot = root, BuiltinAdaptersPath = Path.Combine(root, "none") }),
                NullLogger<AdapterCatalog>.Instance);

            catalog.Reload();

            Assert.NotNull(catalog.Find("valid")?.Help);
            Assert.Equal(3, catalog.Find("valid")!.Help!.Topics.Count);
            Assert.Equal(3, catalog.Problems.Count);
            Assert.All(catalog.Problems, problem => Assert.StartsWith("help.json is not valid JSON: ", Assert.Single(problem.Errors), StringComparison.Ordinal));
            Assert.Contains("testd", Problem(catalog, misspelt), StringComparison.Ordinal);
            Assert.Contains("verified", Problem(catalog, unverified), StringComparison.Ordinal);
            Assert.NotNull(Problem(catalog, broken));
            Assert.NotNull(valid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_api_flags_settings_and_groups_that_have_a_topic()
    {
        var summary = AdapterSummary.From(Adapter());

        Assert.True(summary.Settings.Single(setting => setting.Key == "combat").HasHelp);
        Assert.False(summary.Settings.Single(setting => setting.Key == "name").HasHelp);
        Assert.True(summary.SettingGroups.Single(group => group.Id == "world").HasHelp);
        Assert.False(summary.SettingGroups.Single(group => group.Id == "server").HasHelp);
        Assert.All(AdapterSummary.From(Adapter(help: null)).Settings, setting => Assert.False(setting.HasHelp));
    }

    [Fact]
    public void The_built_in_Valheim_help_covers_the_topics_of_docs_12()
    {
        var catalog = new AdapterCatalog(
            Options.Create(new GameWardenOptions
            {
                DataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "no-data-root"),
                BuiltinAdaptersPath = Path.Combine(AppContext.BaseDirectory, "adapters-builtin"),
            }),
            NullLogger<AdapterCatalog>.Instance);
        catalog.Reload();
        var valheim = catalog.Find("valheim")!;
        var topics = valheim.Help!.Topics;

        Assert.Empty(catalog.Problems);
        Assert.Equal("0.3.3", valheim.Version);
        string[] expected =
        [
            "crossplay", "public", "preset", "group:modifiers", "combat", "deathPenalty", "resources", "raids", "portals",
            "noBuildCost", "playerEvents", "passiveMobs", "noMap", "world", "password", "saveInterval", "backups", "backupShort", "backupLong",
        ];
        Assert.All(expected, key => Assert.Contains(key, topics.Keys));

        // A values table lists every option of its enum setting, so no option is left unexplained.
        foreach (var setting in valheim.Schema!.Settings.Where(setting => setting.Type == "enum"))
        {
            Assert.Equal(
                setting.Options.Select(option => option.Value).Order(),
                topics[setting.Key].Values!.Select(value => value.Value).Order());
        }
    }

    private static void Patch(JsonObject topic, string patch)
    {
        foreach (var (name, value) in JsonNode.Parse(patch)!.AsObject())
        {
            topic[name] = value?.DeepClone();
        }
    }

    private static JsonObject Patched(JsonObject topic, string patch)
    {
        var copy = topic.DeepClone().AsObject();
        Patch(copy, patch);
        return copy;
    }

    private static string WriteAdapter(string root, string id, string help)
    {
        var folder = Path.Combine(root, "adapters", id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "adapter.json"), $$"""
            {
              "schemaVersion": 1, "id": "{{id}}", "name": "Demo", "version": "1.0.0", "level": "L2",
              "source": { "type": "manual" },
              "launch": { "exe": "demo.exe", "args": ["-name", "{settings.name}"] },
              "readiness": { "type": "logRegex", "source": "stdout", "pattern": "ready" },
              "stop": { "method": "ctrl-c" },
              "backup": { "paths": ["{paths.data}"], "mode": "stop" },
              "resources": { "min": { "cpuCores": 1, "ramMb": 1, "diskMb": 1 } }
            }
            """);
        File.WriteAllText(Path.Combine(folder, "settings.schema.json"), Schema);
        File.WriteAllText(Path.Combine(folder, "help.json"), help);
        return folder;
    }

    private static string? Problem(AdapterCatalog catalog, string folder) =>
        catalog.Problems.SingleOrDefault(problem => problem.Folder == folder)?.Errors.Single();
}
