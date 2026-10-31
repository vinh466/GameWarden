using System.Globalization;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Mods;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The built-in Palworld adapter against the template S-P1 captured (docs/14 §4, §6 phase 2).</summary>
[Trait("Category", "Unit")]
public sealed class PalworldAdapterTests
{
    private const string Section = "/Script/Pal.PalGameWorldSettings";
    private const string Field = "OptionSettings";

    private static string Template => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "PalworldTemplate-1.0.5.ini"));

    private static AdapterDefinition Adapter()
    {
        var options = Options.Create(new GameWardenOptions
        {
            DataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "no-data-root"),
            BuiltinAdaptersPath = Path.Combine(AppContext.BaseDirectory, "adapters-builtin"),
        });
        var catalog = new AdapterCatalog(options, NullLogger<AdapterCatalog>.Instance, [new BepInExLoader()]);
        catalog.Reload();
        Assert.Empty(catalog.Problems);
        return catalog.Find("palworld") ?? throw new InvalidOperationException("The built-in Palworld adapter is missing.");
    }

    [Fact]
    public void The_adapter_declares_the_install_launch_and_checks_of_the_design()
    {
        var adapter = Adapter();

        Assert.Equal(("0.1.0", "L2"), (adapter.Version, adapter.Level));
        Assert.Equal(("steamcmd", 2394010), (adapter.Source!.Type, adapter.Source.AppId!.Value));
        Assert.Equal(("game", "udp", 8211, false), (adapter.Ports[0].Name, adapter.Ports[0].Protocol, adapter.Ports[0].Default!.Value, adapter.Ports[0].Internal));
        Assert.Equal(("rest", "tcp", 8212, true), (adapter.Ports[1].Name, adapter.Ports[1].Protocol, adapter.Ports[1].Default!.Value, adapter.Ports[1].Internal));

        var file = Assert.Single(adapter.Files);
        Assert.Equal("settings", file.Name);
        Assert.Equal("ue-struct-ini", file.Format);
        Assert.Equal(Section, file.Section);
        Assert.Equal(Field, file.Field);
        Assert.Contains("PalServer-Win64-Shipping-Cmd.exe", adapter.Launch!.Exe, StringComparison.Ordinal);
        Assert.Equal(
            ["AdminPassword", "PublicPort", "RCONEnabled", "RESTAPIEnabled", "RESTAPIPort", "ServerPassword"],
            file.Fixed!.Keys.Order(StringComparer.Ordinal));

        Assert.Equal(("http", 200, 600, 2), (adapter.Readiness!.Type, adapter.Readiness.Status!.Value, adapter.Readiness.TimeoutSec, adapter.Readiness.IntervalSec!.Value));
        Assert.Equal(("admin", "{secrets.AdminPassword}"), (adapter.Readiness.Auth!.Username, adapter.Readiness.Auth.Password));
        Assert.Equal(("http", 120), (adapter.Stop!.Method, adapter.Stop.TimeoutSec));
        Assert.Equal(2, adapter.Stop.Requests!.Count);
        Assert.Equal(("POST", "http://127.0.0.1:{ports.rest}/v1/api/save"), (adapter.Stop.Requests[0].Method, adapter.Stop.Requests[0].Url));
        Assert.Equal("{paths.server}\\Pal\\Saved\\SaveGames", adapter.Backup!.Paths.Single());
        Assert.Equal(16384, adapter.Resources!.Recommended!.RamMb!.Value);
    }

    [Fact]
    public void Every_template_field_is_a_setting_or_a_fixed_field()
    {
        var adapter = Adapter();
        var schema = adapter.Schema!;
        var template = UeStructIni.Parse(Template, Section, Field)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(118, schema.Settings.Count);
        Assert.Equal(116, schema.Settings.Count(setting => setting.Type != "secret"));
        Assert.Equal(2, schema.Settings.Count(setting => setting.Type == "secret"));
        Assert.Equal(12, schema.Groups.Count);
        Assert.True(template.SetEquals(adapter.Files[0].Fixed!.Keys.Concat(schema.Settings.Select(setting => setting.Key))));
        Assert.All(schema.Settings, setting => Assert.False(string.IsNullOrWhiteSpace(setting.Label!["en"]) || string.IsNullOrWhiteSpace(setting.Label["vi"])));
        Assert.All(schema.Groups, group => Assert.False(string.IsNullOrWhiteSpace(group.Label!["en"]) || string.IsNullOrWhiteSpace(group.Label["vi"])));

        var admin = schema.Find("AdminPassword")!;
        Assert.True(admin.Required);
        Assert.True(admin.Generate);
        Assert.False(schema.Find("ServerPassword")!.Required);
        Assert.Equal(["Steam"], SettingDefinition.ListValues(schema.Find("CrossplayPlatforms")!.DefaultText!));
        Assert.True(schema.Find("DenyTechnologyList")!.Quoted);
        Assert.Empty(schema.Find("DenyTechnologyList")!.Options);
    }

    [Fact]
    public void The_panel_does_not_ask_for_the_generated_admin_password()
    {
        var summary = AdapterSummary.From(Adapter());

        var admin = summary.Settings.Single(setting => setting.Key == "AdminPassword");
        Assert.False(admin.Required);
        Assert.False(admin.Wizard);
        Assert.True(summary.Settings.Single(setting => setting.Key == "ServerName").Wizard);
        Assert.True(summary.Settings.Single(setting => setting.Key == "ServerPassword").Wizard);
        Assert.False(summary.Settings.Single(setting => setting.Key == "ServerPassword").Required);
        Assert.Empty(summary.RequiredSettings);
        Assert.Empty(summary.RequiredSecrets);
    }

    [Fact]
    public void The_schema_defaults_match_the_spike_template()
    {
        var adapter = Adapter();
        var template = UeStructIni.Parse(Template, Section, Field).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        foreach (var setting in adapter.Schema!.Settings.Where(setting => setting.Type != "secret"))
        {
            var raw = template[setting.Key];
            var defaultText = setting.DefaultText!;
            switch (setting.Type)
            {
                case "bool":
                    Assert.Equal(raw == "True", defaultText == "true");
                    break;
                case "int":
                    Assert.Equal(int.Parse(raw, CultureInfo.InvariantCulture), int.Parse(defaultText, CultureInfo.InvariantCulture));
                    break;
                case "number":
                    Assert.Equal(decimal.Parse(raw, CultureInfo.InvariantCulture), decimal.Parse(defaultText, CultureInfo.InvariantCulture));
                    break;
                case "string":
                    Assert.Equal(raw.Trim('"'), defaultText);
                    break;
                case "enum":
                    Assert.Equal(raw, defaultText);
                    break;
                case "list":
                    // The Owner chose Steam only (docs/14 §2); every other list is the template's.
                    var expected = setting.Key == "CrossplayPlatforms" ? ["Steam"] : raw.Trim('(', ')').Split(',', StringSplitOptions.RemoveEmptyEntries);
                    Assert.Equal(expected, SettingDefinition.ListValues(defaultText));
                    break;
                default:
                    Assert.Fail($"Unexpected type {setting.Type} for {setting.Key}");
                    break;
            }
        }

        // The fixed fields are exactly the template fields no setting names.
        var fixedNames = adapter.Files[0].Fixed!.Keys.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(6, fixedNames.Count);
        Assert.DoesNotContain(adapter.Schema.Settings.Where(setting => setting.Type != "secret"), setting => fixedNames.Contains(setting.Key));
    }

    [Fact]
    public void Rendering_with_defaults_keeps_every_template_value_except_the_documented_differences()
    {
        var adapter = Adapter();
        if (!OperatingSystem.IsWindows())
        {
            // The adapter targets Windows (docs/14 §4.1); on the Linux dev box its separators are not path separators.
            var file = adapter.Files[0];
            adapter = adapter with
            {
                Files = [file with { Path = file.Path.Replace('\\', '/'), Template = file.Template.Replace('\\', '/') }],
            };
        }

        var root = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "palworld-" + Guid.NewGuid().ToString("N")[..10]);
        var paths = new InstancePaths(root);
        Directory.CreateDirectory(paths.Server);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "PalworldTemplate-1.0.5.ini"), Path.Combine(paths.Server, "DefaultPalWorldSettings.ini"));
        try
        {
            var settings = LaunchSpecBuilder.Effective(adapter, new Dictionary<string, string>());
            var renderedFiles = FileRenderer.Render(
                adapter,
                paths,
                "palworld-1",
                "Palworld",
                LaunchSpecBuilder.DefaultPorts(adapter),
                settings,
                new Dictionary<string, string> { ["AdminPassword"] = "generated-test" },
                new DirectInstanceFiles());

            var target = Path.Combine(paths.Server, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");
            Assert.Equal(target, Assert.Single(renderedFiles));
            Assert.True(File.Exists(Path.Combine(paths.Config, "PalWorldSettings.ini")));

            var before = UeStructIni.Parse(Template, Section, Field);
            var after = UeStructIni.Parse(File.ReadAllText(target), Section, Field);
            Assert.Equal(before.Select(pair => pair.Key), after.Select(pair => pair.Key));

            var byKey = after.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Assert.Equal("True", byKey["RESTAPIEnabled"]);
            Assert.Equal("8212", byKey["RESTAPIPort"]);
            Assert.Equal($"\"generated-test\"", byKey["AdminPassword"]);
            Assert.Equal("\"\"", byKey["ServerPassword"]);
            Assert.Equal("8211", byKey["PublicPort"]);
            Assert.Equal("False", byKey["RCONEnabled"]);
            Assert.Equal("(Steam)", byKey["CrossplayPlatforms"]);
            Assert.Equal(string.Empty, byKey["DenyTechnologyList"]);

            foreach (var (key, expected) in before)
            {
                if (key is "RESTAPIEnabled" or "RESTAPIPort" or "AdminPassword" or "ServerPassword" or "PublicPort" or "RCONEnabled" or "CrossplayPlatforms" or "DenyTechnologyList")
                {
                    continue;
                }

                Assert.Equal(expected, byKey[key]);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // The test's temp folder; the OS cleans it later.
            }
        }
    }

    [Fact]
    public void The_help_topics_cover_the_designs_subjects()
    {
        var help = Adapter().Help!;

        Assert.Equal(
            ["AdminPassword", "AutoSaveSpan", "CrossplayPlatforms", "DeathPenalty", "bIsPvP", "group:bases", "group:rates", "group:world"],
            help.Topics.Keys.Order(StringComparer.Ordinal));
        Assert.All(help.Topics.Values, topic => Assert.True(topic.Verified));
    }
}
