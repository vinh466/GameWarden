using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using GameWarden.Core.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SettingsSchemaTests
{
    private const string Schema = """
        {
          "groups": [
            { "id": "server", "label": "Server" },
            { "id": "world", "label": { "en": "World", "vi": "Thế giới" }, "advanced": true }
          ],
          "settings": [
            { "key": "name", "type": "string", "group": "server", "required": true },
            { "key": "password", "type": "secret", "group": "server", "required": true, "minLength": 5 },
            { "key": "crossplay", "type": "bool", "group": "server", "default": false, "target": { "kind": "arg", "args": ["-crossplay"] } },
            { "key": "interval", "type": "int", "group": "world", "default": 600, "min": 60, "max": 7200 },
            {
              "key": "combat", "type": "enum", "group": "world", "default": "normal",
              "options": [{ "value": "easy" }, { "value": "normal" }, { "value": "hard" }],
              "target": { "kind": "arg", "args": ["-modifier", "combat", "{settings.combat}"] }
            }
          ]
        }
        """;

    private static AdapterDefinition Adapter => new()
    {
        SchemaVersion = 1,
        Id = "demo",
        Name = "Demo",
        Version = "1.0.0",
        Level = "L2",
        Source = new AdapterSource("manual"),
        Launch = new AdapterLaunch(
            "{paths.server}\\demo.exe",
            ["-name", "{settings.name}", "-password", "{secrets.password}", "-saveinterval", "{settings.interval}"]),
        Readiness = new AdapterReadiness("logRegex", "stdout", "ready"),
        Stop = new AdapterStop("ctrl-c"),
        Backup = new AdapterBackup(["{paths.data}"], "stop"),
        Resources = new AdapterResources(new ResourceAmounts(1, 512, 100)),
        Schema = JsonSerializer.Deserialize<SettingsSchema>(Schema, ContractJson.Options),
        Folder = @"C:\adapters\demo",
    };

    private static string[] Arguments(Dictionary<string, string> settings) =>
        [.. LaunchSpecBuilder.Build(Adapter, "demo-1", "Demo", new InstancePaths(@"C:\GameWarden\instances\demo-1"), new Dictionary<string, int>(), settings, new Dictionary<string, string> { ["password"] = "hunter2" }).Arguments];

    [Fact]
    public void A_complete_schema_has_no_errors()
    {
        Assert.Empty(AdapterCatalog.Validate(Adapter));
    }

    [Fact]
    public void Defaults_fill_the_launch_section_and_need_no_extra_arguments()
    {
        Assert.Equal(["-name", "Vikings", "-password", "hunter2", "-saveinterval", "600"], Arguments(new() { ["name"] = "Vikings" }));
    }

    [Fact]
    public void A_target_adds_its_arguments_when_the_value_differs_from_the_default()
    {
        var arguments = Arguments(new() { ["name"] = "Vikings", ["crossplay"] = "true", ["combat"] = "hard", ["interval"] = "300" });

        Assert.Equal(["-name", "Vikings", "-password", "hunter2", "-saveinterval", "300", "-crossplay", "-modifier", "combat", "hard"], arguments);
        Assert.DoesNotContain("-crossplay", Arguments(new() { ["name"] = "Vikings", ["crossplay"] = "false", ["combat"] = "normal" }));
    }

    [Fact]
    public void Only_settings_without_a_default_are_required()
    {
        var required = LaunchSpecBuilder.Required(Adapter);

        Assert.Equal(["name"], required.Settings);
        Assert.Equal(["password"], required.Secrets);
    }

    [Fact]
    public void Labels_take_a_string_or_one_text_per_language()
    {
        var groups = Adapter.Schema!.Groups;

        Assert.Equal("Server", groups[0].Label!["en"]);
        Assert.Equal("Thế giới", groups[1].Label!["vi"]);
    }

    [Fact]
    public void The_api_describes_every_field_and_asks_the_required_ones_in_the_wizard()
    {
        var summary = AdapterSummary.From(Adapter);

        Assert.Equal(["server", "world"], summary.SettingGroups.Select(group => group.Id));
        var name = Assert.Single(summary.Settings, setting => setting.Key == "name");
        Assert.True(name.Required && name.Wizard);
        var combat = Assert.Single(summary.Settings, setting => setting.Key == "combat");
        Assert.Equal("normal", combat.Default);
        Assert.False(combat.Required || combat.Wizard);
        Assert.Equal("600", Assert.Single(summary.Settings, setting => setting.Key == "interval").Default);
    }

    [Fact]
    public void An_L1_adapter_gets_one_group_of_plain_fields()
    {
        var summary = AdapterSummary.From(Adapter with { Level = "L1", Schema = null });

        Assert.Equal([AdapterSummary.DefaultGroup], summary.SettingGroups.Select(group => group.Id));
        Assert.Equal(["interval", "name", "password"], summary.Settings.Select(setting => setting.Key).Order());
        Assert.All(summary.Settings, setting => Assert.True(setting.Required && setting.Wizard));
    }

    [Theory]
    [InlineData("combat", "brutal", "one of: easy, normal, hard")]
    [InlineData("interval", "10", "from 60 to 7200")]
    [InlineData("interval", "ten", "whole number")]
    [InlineData("crossplay", "yes", "true or false")]
    public void Values_that_do_not_fit_are_refused(string key, string value, string expected)
    {
        var problem = SettingsSchemaValidation.ValueProblem(Adapter.Schema, [KeyValuePair.Create(key, (string?)value)]);

        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_are_checked_apart_and_empty_values_pass()
    {
        Assert.Contains("at least 5", SettingsSchemaValidation.ValueProblem(Adapter.Schema, [KeyValuePair.Create("password", (string?)"abc")], secrets: true), StringComparison.Ordinal);
        Assert.Null(SettingsSchemaValidation.ValueProblem(Adapter.Schema, [KeyValuePair.Create("password", (string?)"abc")]));
        Assert.Null(SettingsSchemaValidation.ValueProblem(Adapter.Schema, [KeyValuePair.Create("interval", (string?)""), KeyValuePair.Create("unknown", (string?)"x")]));
    }

    [Fact]
    public void Schema_mistakes_are_reported()
    {
        var schema = Adapter.Schema! with
        {
            Settings =
            [
                .. Adapter.Schema!.Settings.Where(setting => setting.Key != "interval"),
                new SettingDefinition { Key = "lost", Type = "string", Group = "nowhere" },
                new SettingDefinition { Key = "odd", Type = "int", Group = "world", Default = JsonDocument.Parse("\"x\"").RootElement },
                new SettingDefinition { Key = "token", Type = "secret", Group = "server", Target = new SettingTarget { Kind = "arg", Args = ["{secrets.token}"] } },
                new SettingDefinition { Key = "file", Type = "string", Group = "world", Target = new SettingTarget { Kind = "ini" } },
            ],
        };

        var errors = AdapterCatalog.Validate(Adapter with { Schema = schema });

        Assert.Contains(errors, error => error.Contains("group 'nowhere' is not defined", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'odd': the default does not fit", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'token': a secret has no default and no target", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("target kind 'ini' is not supported yet", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("does not define the setting 'interval'", StringComparison.Ordinal));
    }

    [Fact]
    public void Level_and_schema_go_together()
    {
        Assert.Contains("settings.schema.json is required from L2.", AdapterCatalog.Validate(Adapter with { Schema = null }));
        Assert.Contains(AdapterCatalog.Validate(Adapter with { Level = "L1" }), error => error.Contains("set level to L2", StringComparison.Ordinal));
    }

    [Fact]
    public void The_built_in_Valheim_adapter_passes_its_settings_on()
    {
        var options = Options.Create(new GameWardenOptions
        {
            DataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "no-data-root"),
            BuiltinAdaptersPath = Path.Combine(AppContext.BaseDirectory, "adapters-builtin"),
        });
        var catalog = new AdapterCatalog(options, NullLogger<AdapterCatalog>.Instance);
        catalog.Reload();
        var valheim = catalog.Find("valheim")!;
        var settings = new Dictionary<string, string>
        {
            ["serverName"] = "Vikings",
            ["world"] = "Midgard",
            ["preset"] = "hard",
            ["raids"] = "none",
            ["noMap"] = "true",
        };

        var arguments = LaunchSpecBuilder.Build(
            valheim, "valheim-main", "Vikings", new InstancePaths(@"C:\GameWarden\instances\valheim-main"),
            LaunchSpecBuilder.DefaultPorts(valheim), settings, new Dictionary<string, string> { ["password"] = "skal-2026" }).Arguments;

        Assert.Equal("L2", valheim.Level);
        Assert.Equal(["serverName", "world"], LaunchSpecBuilder.Required(valheim).Settings);
        Assert.Equal("600", arguments[arguments.ToList().IndexOf("-saveinterval") + 1]);
        Assert.Equal(["-preset", "hard", "-modifier", "raids", "none", "-setkey", "nomap"], arguments.TakeLast(7));
        Assert.DoesNotContain("-crossplay", arguments);
    }
}
