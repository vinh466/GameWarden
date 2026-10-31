using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The Core model ADR-0022 adds for Palworld: declared files, <c>number</c>/<c>list</c>, http checks and stops, internal ports, generated secrets.</summary>
[Trait("Category", "Unit")]
public sealed class FileModelTests
{
    private const string Schema = """
        {
          "groups": [
            { "id": "server", "label": "Server" },
            { "id": "world", "label": "World" }
          ],
          "settings": [
            { "key": "executable", "type": "string", "group": "server", "required": true },
            { "key": "ServerName", "type": "string", "group": "server", "default": "Default Palworld Server", "target": { "kind": "file", "file": "settings", "field": "ServerName" } },
            { "key": "ExpRate", "type": "number", "group": "world", "default": 1, "min": 0.1, "max": 20, "step": 0.1, "target": { "kind": "file", "file": "settings", "field": "ExpRate" } },
            { "key": "ServerPlayerMaxNum", "type": "int", "group": "server", "default": 32, "min": 1, "max": 32, "target": { "kind": "file", "file": "settings" } },
            { "key": "bIsUseBackupSaveData", "type": "bool", "group": "world", "default": true, "target": { "kind": "file", "file": "settings" } },
            { "key": "Difficulty", "type": "enum", "group": "world", "default": "None", "options": [{ "value": "None" }, { "value": "Normal" }, { "value": "Hard" }], "target": { "kind": "file", "file": "settings" } },
            { "key": "CrossplayPlatforms", "type": "list", "group": "server", "default": ["Steam"], "options": [{ "value": "Steam" }, { "value": "Xbox" }, { "value": "PS5" }, { "value": "Mac" }], "target": { "kind": "file", "file": "settings" } },
            { "key": "AdminPassword", "type": "secret", "group": "server", "required": true, "generate": true },
            { "key": "ServerPassword", "type": "secret", "group": "server" }
          ]
        }
        """;

    private static AdapterDefinition Adapter => new()
    {
        SchemaVersion = 1,
        Id = "pwfake",
        Name = "Palworld (fake)",
        Version = "0.1.0",
        Level = "L2",
        Source = new AdapterSource("manual"),
        Launch = new AdapterLaunch(
            "{settings.executable}",
            ["--game-port", "{ports.game}", "--rest-port", "{ports.rest}"]),
        Ports = [new AdapterPort("game", "udp", 8211), new AdapterPort("rest", "tcp", 8212, Internal: true)],
        Files =
        [
            new AdapterFile(
                "settings",
                "{paths.server}/PalWorldSettings.ini",
                "{paths.server}/DefaultPalWorldSettings.ini",
                "ue-struct-ini",
                "/Script/Pal.PalGameWorldSettings",
                "OptionSettings",
                new Dictionary<string, string>
                {
                    ["RESTAPIEnabled"] = "True",
                    ["RESTAPIPort"] = "{ports.rest}",
                    ["AdminPassword"] = "\"{secrets.AdminPassword}\"",
                    ["ServerPassword"] = "\"{secrets.ServerPassword}\"",
                }),
        ],
        Readiness = new AdapterReadiness(
            "http",
            Url: "http://127.0.0.1:{ports.rest}/v1/api/info",
            Status: 200,
            Auth: new AdapterAuth("admin", "{secrets.AdminPassword}"),
            TimeoutSec: 30,
            IntervalSec: 1),
        Stop = new AdapterStop(
            "http",
            TimeoutSec: 20,
            Requests:
            [
                new AdapterHttpRequest("POST", "http://127.0.0.1:{ports.rest}/v1/api/save"),
                new AdapterHttpRequest("POST", "http://127.0.0.1:{ports.rest}/v1/api/shutdown", "{\"waittime\": 1}"),
            ],
            Auth: new AdapterAuth("admin", "{secrets.AdminPassword}")),
        Backup = new AdapterBackup(["{paths.data}"], "stop"),
        Resources = new AdapterResources(new ResourceAmounts(1, 512, 100)),
        Schema = JsonSerializer.Deserialize<SettingsSchema>(Schema, ContractJson.Options),
        Folder = "/adapters/pwfake",
    };

    [Fact]
    public void The_full_model_has_no_errors()
    {
        Assert.Empty(AdapterCatalog.Validate(Adapter));
    }

    [Fact]
    public void A_generated_secret_is_required_and_an_optional_file_secret_is_not()
    {
        var required = LaunchSpecBuilder.Required(Adapter);

        Assert.Equal(["executable"], required.Settings);
        Assert.Equal(["AdminPassword"], required.Secrets);
    }

    [Fact]
    public void A_file_setting_without_a_default_is_required()
    {
        var schema = Adapter.Schema! with
        {
            Settings = [.. Adapter.Schema!.Settings, new SettingDefinition { Key = "NoDefault", Type = "string", Group = "world", Target = new SettingTarget { Kind = "file", File = "settings" } }],
        };

        Assert.Contains("NoDefault", LaunchSpecBuilder.Required(Adapter with { Schema = schema }).Settings);
    }

    [Fact]
    public void Number_and_list_values_are_checked()
    {
        var schema = Adapter.Schema!;

        Assert.Null(schema.Find("ExpRate")!.Check("1.5"));
        Assert.Contains("must be a number", schema.Find("ExpRate")!.Check("fast"), StringComparison.Ordinal);
        Assert.Contains("from 0.1 to 20", schema.Find("ExpRate")!.Check("25"), StringComparison.Ordinal);
        Assert.Null(schema.Find("CrossplayPlatforms")!.Check("Steam,Xbox"));
        Assert.Contains("not one of", schema.Find("CrossplayPlatforms")!.Check("Steam,Switch"), StringComparison.Ordinal);
        Assert.Contains("empty list item", schema.Find("CrossplayPlatforms")!.Check("Steam,,Xbox"), StringComparison.Ordinal);
    }

    [Fact]
    public void List_defaults_are_stored_as_comma_separated_text()
    {
        Assert.Equal("Steam", Adapter.Schema!.Find("CrossplayPlatforms")!.DefaultText);
        Assert.Equal(["Steam", "Xbox"], SettingDefinition.ListValues("Steam,Xbox"));
    }

    [Fact]
    public void A_file_string_may_not_carry_a_quote_or_a_line_break()
    {
        var setting = Adapter.Schema!.Find("ServerName")!;

        Assert.Contains("double quote", setting.Check("My \"server\""), StringComparison.Ordinal);
        Assert.Contains("line break", setting.Check("My\nserver"), StringComparison.Ordinal);
        Assert.Null(setting.Check("My server"));
    }

    [Theory]
    [InlineData("http readiness without a URL", "readiness.url is required")]
    [InlineData("http stop without requests", "stop.requests needs at least one")]
    [InlineData("unknown file format", "format must be")]
    [InlineData("file target with no file", "not a declared file")]
    [InlineData("setting on a fixed field", "is fixed in file")]
    [InlineData("number default outside its range", "default does not fit")]
    [InlineData("list default with an unknown option", "default does not fit")]
    [InlineData("generate on a string", "generate is for secrets only")]
    public void Model_mistakes_are_reported(string what, string expected)
    {
        var adapter = Adapter;
        switch (what)
        {
            case "http readiness without a URL":
                adapter = adapter with { Readiness = adapter.Readiness! with { Url = null } };
                break;
            case "http stop without requests":
                adapter = adapter with { Stop = adapter.Stop! with { Requests = [] } };
                break;
            case "unknown file format":
                adapter = adapter with { Files = [adapter.Files[0] with { Format = "yaml" }] };
                break;
            case "file target with no file":
                adapter = adapter with { Files = [] };
                break;
            case "setting on a fixed field":
                adapter = adapter with
                {
                    Schema = adapter.Schema! with
                    {
                        Settings = [.. adapter.Schema!.Settings, new SettingDefinition { Key = "RESTAPIEnabled", Type = "bool", Group = "server", Target = new SettingTarget { Kind = "file", File = "settings" } }],
                    },
                };
                break;
            case "number default outside its range":
                adapter = adapter with
                {
                    Schema = Replace(adapter.Schema!, "ExpRate", setting => setting with { Default = JsonDocument.Parse("99").RootElement }),
                };
                break;
            case "list default with an unknown option":
                adapter = adapter with
                {
                    Schema = Replace(adapter.Schema!, "CrossplayPlatforms", setting => setting with { Default = JsonDocument.Parse("[\"Switch\"]").RootElement }),
                };
                break;
            case "generate on a string":
                adapter = adapter with
                {
                    Schema = Replace(adapter.Schema!, "ServerName", setting => setting with { Generate = true }),
                };
                break;
        }

        Assert.Contains(AdapterCatalog.Validate(adapter), error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_template_says_to_install_the_game()
    {
        var paths = new InstancePaths(Path.Combine(Path.GetTempPath(), "gamewarden-tests", "pw-" + Guid.NewGuid().ToString("N")[..10]));

        var error = Assert.Throws<InvalidOperationException>(() => FileRenderer.Render(
            Adapter,
            paths,
            "pw-1",
            "Palworld",
            LaunchSpecBuilder.DefaultPorts(Adapter),
            new Dictionary<string, string> { ["executable"] = "PalServer.exe" },
            new Dictionary<string, string>(),
            new DirectInstanceFiles()));

        Assert.Contains("template file was not found", error.Message, StringComparison.Ordinal);
        Assert.Contains("Install the game first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_written_to_a_file_may_not_carry_a_quote_or_a_line_break()
    {
        var problem = FileRenderer.Problem(Adapter, new Dictionary<string, string> { ["ServerPassword"] = "a\"b" });
        var newline = FileRenderer.Problem(Adapter, new Dictionary<string, string> { ["AdminPassword"] = "a\nb" });

        Assert.Contains("double quote", problem, StringComparison.Ordinal);
        Assert.Contains("line break", newline, StringComparison.Ordinal);
        Assert.Null(FileRenderer.Problem(Adapter, new Dictionary<string, string> { ["AdminPassword"] = "safe" }));
    }

    [Fact]
    public void The_panel_sees_the_number_step_the_internal_port_and_the_bounds()
    {
        var summary = AdapterSummary.From(Adapter);

        Assert.Equal(0.1m, summary.Settings.Single(setting => setting.Key == "ExpRate").Step);
        Assert.Equal(0.1m, summary.Settings.Single(setting => setting.Key == "ExpRate").Min);
        Assert.True(summary.Ports.Single(port => port.Name == "rest").Internal);
        Assert.False(summary.Ports.Single(port => port.Name == "game").Internal);
    }

    private static SettingsSchema Replace(SettingsSchema schema, string key, Func<SettingDefinition, SettingDefinition> change) =>
        schema with
        {
            Settings = [.. schema.Settings.Select(setting => setting.Key == key ? change(setting) : setting)],
        };
}
