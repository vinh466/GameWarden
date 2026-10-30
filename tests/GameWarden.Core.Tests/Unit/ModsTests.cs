using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Core.Adapters;
using GameWarden.Core.Mods;

namespace GameWarden.Core.Tests.Unit;

/// <summary>Package names, the set layout and its path checks, and the adapter's <c>mods</c> section (docs/13 §4.5, §4.12).</summary>
[Trait("Category", "Unit")]
public sealed class ModsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "set-1");

    private static AdapterDefinition ModdedAdapter => new()
    {
        SchemaVersion = 1,
        Id = "modded",
        Name = "Modded",
        Version = "1.0.0",
        Level = "L3",
        Source = new AdapterSource("manual"),
        Launch = new AdapterLaunch("demo.exe"),
        Readiness = new AdapterReadiness("logRegex", "stdout", "ready"),
        Stop = new AdapterStop("ctrl-c"),
        Backup = new AdapterBackup(["{paths.data}"], "stop"),
        Resources = new AdapterResources(new ResourceAmounts(1, 1, 1)),
        Schema = JsonSerializer.Deserialize<SettingsSchema>(
            """{ "groups": [{ "id": "server", "label": "Server" }], "settings": [{ "key": "name", "type": "string", "group": "server", "default": "x" }] }""",
            ContractJson.Options),
        Mods = new AdapterMods("fake", "denikson-BepInExPack_Valheim", [new AdapterModSource("thunderstore", "valheim")]),
        Folder = "/adapters/modded",
    };

    [Theory]
    [InlineData("RandyKnapp-EpicLoot", true)]
    [InlineData("denikson-BepInExPack_Valheim", true)]
    [InlineData("EpicLoot", false)]
    [InlineData("Randy-Knapp-EpicLoot", false)]
    [InlineData("../EpicLoot", false)]
    [InlineData("Randy Knapp-EpicLoot", false)]
    public void Package_names_are_Namespace_Name(string package, bool valid)
    {
        Assert.Equal(valid, Packages.IsPackage(package));
    }

    [Fact]
    public void Dependencies_split_into_package_and_version_and_versions_compare_by_number()
    {
        Assert.True(Packages.TryParseDependency("denikson-BepInExPack_Valheim-5.4.2351", out var dependency));
        Assert.Equal(new PackageRef("denikson-BepInExPack_Valheim", "5.4.2351"), dependency);
        Assert.False(Packages.TryParseDependency("denikson-BepInExPack_Valheim", out _));
        Assert.False(Packages.TryParseDependency("denikson-BepInExPack_Valheim-5.4", out _));
        Assert.False(Packages.TryParseDependency("a-b-c-1.0.0", out _));
        Assert.True(Packages.CompareVersions("1.10.0", "1.9.0") > 0);
        Assert.False(Packages.IsVersion("1.2.3-beta"));
    }

    [Theory]
    [InlineData("plugins/EpicLoot.dll", "plugins/EpicLoot.dll")]
    [InlineData("plugins\\sub\\EpicLoot.dll", "plugins/sub/EpicLoot.dll")]
    [InlineData("./plugins//EpicLoot.dll", "plugins/EpicLoot.dll")]
    [InlineData("plugins/", null)]
    public void Zip_entry_names_are_normalized(string entry, string? expected)
    {
        Assert.Equal(expected, ModLayout.Normalize(entry));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("plugins/../../evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("plugins/file.dll:stream")]
    public void Entries_that_leave_their_folder_or_are_absolute_are_refused(string entry)
    {
        Assert.Throws<InvalidDataException>(() => ModLayout.Normalize(entry));
        Assert.Throws<InvalidDataException>(() => ModLayout.Resolve(Root, entry));
    }

    [Fact]
    public void Targets_stay_inside_the_set_folder_and_under_the_path_limit()
    {
        var target = ModLayout.Resolve(Root, "plugins/RandyKnapp-EpicLoot/EpicLoot.dll");

        Assert.Equal(Path.Combine(Root, "plugins", "RandyKnapp-EpicLoot", "EpicLoot.dll"), target);
        var tooLong = string.Join('/', Enumerable.Repeat("folder", 50)) + "/file.dll";
        var error = Assert.Throws<InvalidDataException>(() => ModLayout.Resolve(Root, tooLong));
        Assert.Contains($"limit is {ModLayout.MaxPathLength}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_loader_places_files_case_sensitively_and_leaves_package_files_out()
    {
        var loader = new FakeModLoader();

        Assert.Equal("loader/BepInEx/core/BepInEx.Preloader.dll", loader.Place(FakeThunderstore.Loader, isLoaderPackage: true, "BepInEx/core/BepInEx.Preloader.dll"));
        Assert.Equal("plugins/RandyKnapp-EpicLoot/EpicLoot.dll", loader.Place("RandyKnapp-EpicLoot", isLoaderPackage: false, "EpicLoot.dll"));
        Assert.Null(loader.Place("RandyKnapp-EpicLoot", isLoaderPackage: false, "manifest.json"));
        Assert.Equal("plugins/RandyKnapp-EpicLoot/Manifest.json", loader.Place("RandyKnapp-EpicLoot", isLoaderPackage: false, "Manifest.json"));
        Assert.NotEqual(
            ModLayout.Resolve(Root, loader.Place("A-B", false, "Plugin.dll")!),
            ModLayout.Resolve(Root, loader.Place("A-B", false, "plugin.dll")!));
    }

    [Fact]
    public void An_L3_adapter_with_a_known_loader_is_valid()
    {
        Assert.Empty(AdapterCatalog.Validate(ModdedAdapter, loaders: ["fake"]));
    }

    [Fact]
    public void The_mods_section_is_checked()
    {
        Assert.Contains("mods is required at L3.", AdapterCatalog.Validate(ModdedAdapter with { Mods = null }, loaders: ["fake"]));
        Assert.Contains("an adapter with mods is L3; set level to L3.", AdapterCatalog.Validate(ModdedAdapter with { Level = "L2" }, loaders: ["fake"]));
        Assert.Contains(AdapterCatalog.Validate(ModdedAdapter), error => error.Contains("has no mod loader", StringComparison.Ordinal));
        Assert.Contains(
            AdapterCatalog.Validate(ModdedAdapter with { Mods = ModdedAdapter.Mods! with { Loader = "bepinex" } }, loaders: ["fake"]),
            error => error == "mods.loader: unknown loader 'bepinex' (known: fake).");

        var errors = AdapterCatalog.Validate(
            ModdedAdapter with
            {
                Mods = new AdapterMods("fake", "Not a package", [new AdapterModSource("thunderstore"), new AdapterModSource("curseforge")]),
            },
            loaders: ["fake"]);
        Assert.Contains("mods.loaderPackage must be a package name, Namespace-Name.", errors);
        Assert.Contains("mods.sources[0]: thunderstore needs a community (e.g. valheim).", errors);
        Assert.Contains("mods.sources[1]: unknown type 'curseforge' (known: thunderstore).", errors);
        Assert.Contains("mods.sources needs at least one source.", AdapterCatalog.Validate(ModdedAdapter with { Mods = new AdapterMods("fake") }, loaders: ["fake"]));
    }
}
