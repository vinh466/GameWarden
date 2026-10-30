using GameWarden.Core.Adapters;
using GameWarden.Core.Instances;
using GameWarden.Core.Mods;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class AdapterTests
{
    private static AdapterDefinition Valid => new()
    {
        SchemaVersion = 1,
        Id = "demo",
        Name = "Demo",
        Version = "1.0.0",
        Level = "L1",
        Source = new AdapterSource("manual"),
        Launch = new AdapterLaunch("{paths.server}\\demo.exe", ["-name", "{settings.name}", "-port", "{ports.game}"]),
        Ports = [new AdapterPort("game", "udp", 2456), new AdapterPort("query", "udp", Offset: new PortOffset("game", 1))],
        Readiness = new AdapterReadiness("logRegex", "stdout", "ready"),
        Stop = new AdapterStop("ctrl-c"),
        Backup = new AdapterBackup(["{paths.data}"], "stop"),
        Resources = new AdapterResources(new ResourceAmounts(1, 512, 100)),
        Folder = @"C:\adapters\demo",
    };

    [Fact]
    public void Built_in_adapters_are_valid()
    {
        var options = Options.Create(new GameWardenOptions
        {
            DataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "no-data-root"),
            BuiltinAdaptersPath = Path.Combine(AppContext.BaseDirectory, "adapters-builtin"),
        });
        var catalog = new AdapterCatalog(options, NullLogger<AdapterCatalog>.Instance, [new BepInExLoader()]);

        catalog.Reload();

        Assert.Empty(catalog.Problems);
        Assert.Contains(catalog.Adapters, adapter => adapter.Id == "fakegame");
        Assert.Contains(catalog.Adapters, adapter => adapter.Id == "valheim");
        Assert.All(catalog.Adapters, adapter => Assert.True(adapter.IsBuiltIn));

        // Valheim is L3 since 0.4.0: BepInEx from Thunderstore's Valheim community (docs/13 §4.1).
        var valheim = catalog.Find("valheim")!;
        Assert.Equal(("0.4.0", "L3"), (valheim.Version, valheim.Level));
        Assert.Equal(BepInExLoader.LoaderId, valheim.Mods!.Loader);
        Assert.Equal("denikson-BepInExPack_Valheim", valheim.Mods.LoaderPackage);
        Assert.Equal([new AdapterModSource("thunderstore", "valheim")], valheim.Mods.Sources!);
    }

    [Fact]
    public void A_complete_L1_adapter_has_no_errors()
    {
        Assert.Empty(AdapterCatalog.Validate(Valid));
    }

    [Fact]
    public void Missing_parts_are_reported()
    {
        var errors = AdapterCatalog.Validate(Valid with { Id = "Bad Id", Stop = new AdapterStop("stdin"), Resources = null });

        Assert.Contains(errors, error => error.StartsWith("id ", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("stop.command", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("resources.min", StringComparison.Ordinal));
    }

    [Fact]
    public void Launch_spec_keeps_each_argument_whole()
    {
        var paths = new InstancePaths(@"C:\GameWarden\instances\demo-1");
        var ports = LaunchSpecBuilder.DefaultPorts(Valid);
        var settings = new Dictionary<string, string> { ["name"] = "My \"server\" -public 1" };

        var spec = LaunchSpecBuilder.Build(Valid, "demo-1", "Demo", paths, ports, settings);

        Assert.Equal(paths.Server + @"\demo.exe", spec.Executable); // the adapter's own separator, as written
        Assert.Equal(["-name", "My \"server\" -public 1", "-port", "2456"], spec.Arguments);
        Assert.Equal(2457, ports["query"]);
        Assert.Equal(paths.RunnerState, spec.StateDirectory);
    }

    [Fact]
    public void Unknown_placeholders_are_errors()
    {
        var adapter = Valid with { Launch = new AdapterLaunch("{paths.server}\\demo.exe", ["{secrets.password}"]) };

        var error = Assert.Throws<InvalidOperationException>(() =>
            LaunchSpecBuilder.Build(adapter, "demo-1", "Demo", new InstancePaths(@"C:\x"), LaunchSpecBuilder.DefaultPorts(adapter), new Dictionary<string, string>()));
        Assert.Contains("{secrets.password}", error.Message, StringComparison.Ordinal);
    }
}
