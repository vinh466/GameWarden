using System.Text;
using GameWarden.Core.Mods;

namespace GameWarden.Core.Tests.Unit;

/// <summary>
/// The BepInEx loader's layout (docs/13 §4.5, spike S-M2): where r2modman would put each file of the real zips, compared
/// case-sensitively as on Linux (§4.12). The entry names were read from the zips Thunderstore served on 2026-10-30.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BepInExLoaderTests
{
    private const string Pack = "denikson-BepInExPack_Valheim";

    private static readonly BepInExLoader Loader = new();

    /// <summary><c>denikson-BepInExPack_Valheim-5.4.2360.zip</c>.</summary>
    private static readonly string[] PackEntries =
    [
        "CHANGELOG.md", "README.md", "icon.png",
        "BepInExPack_Valheim/", "BepInExPack_Valheim/.doorstop_version", "BepInExPack_Valheim/changelog.txt",
        "BepInExPack_Valheim/doorstop_config.ini", "BepInExPack_Valheim/start_game_bepinex.sh", "BepInExPack_Valheim/start_server_bepinex.sh",
        "BepInExPack_Valheim/winhttp.dll",
        "BepInExPack_Valheim/BepInEx/", "BepInExPack_Valheim/BepInEx/config/", "BepInExPack_Valheim/BepInEx/config/BepInEx.cfg",
        "BepInExPack_Valheim/BepInEx/core/", "BepInExPack_Valheim/BepInEx/core/0Harmony.dll", "BepInExPack_Valheim/BepInEx/core/0Harmony.xml",
        "BepInExPack_Valheim/BepInEx/core/0Harmony20.dll", "BepInExPack_Valheim/BepInEx/core/BepInEx.Harmony.dll",
        "BepInExPack_Valheim/BepInEx/core/BepInEx.Harmony.xml", "BepInExPack_Valheim/BepInEx/core/BepInEx.Preloader.dll",
        "BepInExPack_Valheim/BepInEx/core/BepInEx.Preloader.xml", "BepInExPack_Valheim/BepInEx/core/BepInEx.dll",
        "BepInExPack_Valheim/BepInEx/core/BepInEx.xml", "BepInExPack_Valheim/BepInEx/core/HarmonyXInterop.dll",
        "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.Mdb.dll", "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.Pdb.dll",
        "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.Rocks.dll", "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.dll",
        "BepInExPack_Valheim/BepInEx/core/MonoMod.RuntimeDetour.dll", "BepInExPack_Valheim/BepInEx/core/MonoMod.RuntimeDetour.xml",
        "BepInExPack_Valheim/BepInEx/core/MonoMod.Utils.dll", "BepInExPack_Valheim/BepInEx/core/MonoMod.Utils.xml",
        "BepInExPack_Valheim/doorstop_libs/", "BepInExPack_Valheim/doorstop_libs/libdoorstop_x64.dylib",
        "BepInExPack_Valheim/doorstop_libs/libdoorstop_x64.so",
        "manifest.json",
    ];

    /// <summary><c>RandyKnapp-EpicLoot-0.15.4.zip</c>.</summary>
    private static readonly string[] EpicLootEntries = ["CHANGELOG.md", "README.md", "icon.png", "manifest.json", "plugins/", "plugins/EpicLoot.dll"];

    /// <summary><c>ValheimModding-Jotunn-2.30.2.zip</c>: its entries use <c>\</c>.</summary>
    private static readonly string[] JotunnEntries =
    [
        "plugins\\Jotunn.dll", "plugins\\Jotunn.dll.mdb", "plugins\\Jotunn.pdb", "plugins\\Jotunn.xml",
        "CHANGELOG.md", "icon.png", "manifest.json", "README.md",
    ];

    /// <summary><c>ValheimModding-JsonDotNET-13.0.4.zip</c>.</summary>
    private static readonly string[] JsonDotNetEntries =
    [
        "LICENSE.md", "manifest.json", "README.md", "plugins/", "plugins/Newtonsoft.Json.dll", "plugins/NewtonsoftJsonDetector.dll", "icon.png",
    ];

    /// <summary><c>SephrinMods-VikingNPC_Continued-0.4.2.zip</c> (Norsemen): its DLL at the root.</summary>
    private static readonly string[] VikingNpcEntries = ["README.md", "icon.png", "CHANGELOG.md", "manifest.json", "Norsemen.dll"];

    [Fact]
    public void The_pack_puts_BepInEx_in_the_set_and_the_two_Doorstop_files_in_loader()
    {
        var placed = Placements(Pack, isLoaderPackage: true, PackEntries);

        Assert.Equal(
            [
                "BepInExPack_Valheim/doorstop_config.ini -> loader/doorstop_config.ini",
                "BepInExPack_Valheim/winhttp.dll -> loader/winhttp.dll",
                "BepInExPack_Valheim/BepInEx/config/BepInEx.cfg -> BepInEx/config/BepInEx.cfg",
                "BepInExPack_Valheim/BepInEx/core/0Harmony.dll -> BepInEx/core/0Harmony.dll",
                "BepInExPack_Valheim/BepInEx/core/0Harmony.xml -> BepInEx/core/0Harmony.xml",
                "BepInExPack_Valheim/BepInEx/core/0Harmony20.dll -> BepInEx/core/0Harmony20.dll",
                "BepInExPack_Valheim/BepInEx/core/BepInEx.Harmony.dll -> BepInEx/core/BepInEx.Harmony.dll",
                "BepInExPack_Valheim/BepInEx/core/BepInEx.Harmony.xml -> BepInEx/core/BepInEx.Harmony.xml",
                "BepInExPack_Valheim/BepInEx/core/BepInEx.Preloader.dll -> BepInEx/core/BepInEx.Preloader.dll",
                "BepInExPack_Valheim/BepInEx/core/BepInEx.Preloader.xml -> BepInEx/core/BepInEx.Preloader.xml",
                "BepInExPack_Valheim/BepInEx/core/BepInEx.dll -> BepInEx/core/BepInEx.dll",
                "BepInExPack_Valheim/BepInEx/core/BepInEx.xml -> BepInEx/core/BepInEx.xml",
                "BepInExPack_Valheim/BepInEx/core/HarmonyXInterop.dll -> BepInEx/core/HarmonyXInterop.dll",
                "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.Mdb.dll -> BepInEx/core/Mono.Cecil.Mdb.dll",
                "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.Pdb.dll -> BepInEx/core/Mono.Cecil.Pdb.dll",
                "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.Rocks.dll -> BepInEx/core/Mono.Cecil.Rocks.dll",
                "BepInExPack_Valheim/BepInEx/core/Mono.Cecil.dll -> BepInEx/core/Mono.Cecil.dll",
                "BepInExPack_Valheim/BepInEx/core/MonoMod.RuntimeDetour.dll -> BepInEx/core/MonoMod.RuntimeDetour.dll",
                "BepInExPack_Valheim/BepInEx/core/MonoMod.RuntimeDetour.xml -> BepInEx/core/MonoMod.RuntimeDetour.xml",
                "BepInExPack_Valheim/BepInEx/core/MonoMod.Utils.dll -> BepInEx/core/MonoMod.Utils.dll",
                "BepInExPack_Valheim/BepInEx/core/MonoMod.Utils.xml -> BepInEx/core/MonoMod.Utils.xml",
            ],
            placed);

        // Thunderstore's files, the changelog, the Linux and macOS launch scripts and Doorstop's libraries stay out.
        Assert.Null(Loader.Place(Pack, true, "manifest.json"));
        Assert.Null(Loader.Place(Pack, true, "BepInExPack_Valheim/start_server_bepinex.sh"));
        Assert.Null(Loader.Place(Pack, true, "BepInExPack_Valheim/doorstop_libs/libdoorstop_x64.so"));
    }

    [Fact]
    public void Epic_Loot_goes_into_its_plugin_folder_with_Thunderstores_files()
    {
        Assert.Equal(
            [
                "CHANGELOG.md -> BepInEx/plugins/RandyKnapp-EpicLoot/CHANGELOG.md",
                "README.md -> BepInEx/plugins/RandyKnapp-EpicLoot/README.md",
                "icon.png -> BepInEx/plugins/RandyKnapp-EpicLoot/icon.png",
                "manifest.json -> BepInEx/plugins/RandyKnapp-EpicLoot/manifest.json",
                "plugins/EpicLoot.dll -> BepInEx/plugins/RandyKnapp-EpicLoot/EpicLoot.dll",
            ],
            Placements("RandyKnapp-EpicLoot", isLoaderPackage: false, EpicLootEntries));
    }

    [Fact]
    public void Jotunn_entries_with_backslashes_go_into_its_plugin_folder()
    {
        Assert.Equal(
            [
                "plugins/Jotunn.dll -> BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll",
                "plugins/Jotunn.dll.mdb -> BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll.mdb",
                "plugins/Jotunn.pdb -> BepInEx/plugins/ValheimModding-Jotunn/Jotunn.pdb",
                "plugins/Jotunn.xml -> BepInEx/plugins/ValheimModding-Jotunn/Jotunn.xml",
                "CHANGELOG.md -> BepInEx/plugins/ValheimModding-Jotunn/CHANGELOG.md",
                "icon.png -> BepInEx/plugins/ValheimModding-Jotunn/icon.png",
                "manifest.json -> BepInEx/plugins/ValheimModding-Jotunn/manifest.json",
                "README.md -> BepInEx/plugins/ValheimModding-Jotunn/README.md",
            ],
            Placements("ValheimModding-Jotunn", isLoaderPackage: false, JotunnEntries));
    }

    [Fact]
    public void JsonDotNET_goes_into_its_plugin_folder()
    {
        Assert.Equal(
            [
                "LICENSE.md -> BepInEx/plugins/ValheimModding-JsonDotNET/LICENSE.md",
                "manifest.json -> BepInEx/plugins/ValheimModding-JsonDotNET/manifest.json",
                "README.md -> BepInEx/plugins/ValheimModding-JsonDotNET/README.md",
                "plugins/Newtonsoft.Json.dll -> BepInEx/plugins/ValheimModding-JsonDotNET/Newtonsoft.Json.dll",
                "plugins/NewtonsoftJsonDetector.dll -> BepInEx/plugins/ValheimModding-JsonDotNET/NewtonsoftJsonDetector.dll",
                "icon.png -> BepInEx/plugins/ValheimModding-JsonDotNET/icon.png",
            ],
            Placements("ValheimModding-JsonDotNET", isLoaderPackage: false, JsonDotNetEntries));
    }

    [Fact]
    public void Norsemen_with_its_dll_at_the_root_goes_into_its_plugin_folder()
    {
        Assert.Equal(
            [
                "README.md -> BepInEx/plugins/SephrinMods-VikingNPC_Continued/README.md",
                "icon.png -> BepInEx/plugins/SephrinMods-VikingNPC_Continued/icon.png",
                "CHANGELOG.md -> BepInEx/plugins/SephrinMods-VikingNPC_Continued/CHANGELOG.md",
                "manifest.json -> BepInEx/plugins/SephrinMods-VikingNPC_Continued/manifest.json",
                "Norsemen.dll -> BepInEx/plugins/SephrinMods-VikingNPC_Continued/Norsemen.dll",
            ],
            Placements("SephrinMods-VikingNPC_Continued", isLoaderPackage: false, VikingNpcEntries));
    }

    [Theory]
    [InlineData("patchers/Early.dll", "BepInEx/patchers/A-B/Early.dll")]
    [InlineData("core/Extra.dll", "BepInEx/core/A-B/Extra.dll")]
    [InlineData("monomod/Assembly-CSharp.A.mm.dll", "BepInEx/monomod/A-B/Assembly-CSharp.A.mm.dll")]
    [InlineData("Assembly-CSharp.A.mm.dll", "BepInEx/monomod/A-B/Assembly-CSharp.A.mm.dll")]
    [InlineData("config/a.cfg", "BepInEx/config/a.cfg")]
    [InlineData("config/A/sub/b.yml", "BepInEx/config/A/sub/b.yml")]
    [InlineData("BepInEx/config/a.cfg", "BepInEx/config/a.cfg")]
    [InlineData("BepInEx/plugins/Translations/en.json", "BepInEx/plugins/A-B/Translations/en.json")]
    [InlineData("Plugins/A.dll", "BepInEx/plugins/A-B/A.dll")]
    [InlineData("plugins/config/inside.cfg", "BepInEx/plugins/A-B/config/inside.cfg")]
    [InlineData("SlimVML/Old.dll", "BepInEx/SlimVML/A-B/Old.dll")]
    [InlineData("Assets/Bundles/items", "BepInEx/plugins/A-B/items")]
    [InlineData("Assets/A.dll", "BepInEx/plugins/A-B/A.dll")]
    public void Other_layouts_follow_r2modmans_rules(string entry, string expected)
    {
        Assert.Equal(expected, Loader.Place("A-B", isLoaderPackage: false, entry));
    }

    [Fact]
    public void Doorstop_is_turned_off_in_the_server_folders_ini_and_everything_else_is_kept()
    {
        var config = new LoaderEntryFile("loader/doorstop_config.ini", BepInExLoader.DoorstopConfig);

        var text = Encoding.UTF8.GetString(Loader.EntryFileContent(config, Encoding.UTF8.GetBytes(FakeThunderstore.DoorstopConfig)));

        Assert.Equal(FakeThunderstore.DoorstopConfig.Replace("enabled = true", "enabled = false", StringComparison.Ordinal), text);
        Assert.Contains("\r\ndebug_enabled = false\r\n", text, StringComparison.Ordinal);
        Assert.Equal("[General]\nenabled = false", Encoding.UTF8.GetString(Loader.EntryFileContent(config, "[General]\nEnabled=TRUE"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => Loader.EntryFileContent(config, "[General]\r\n"u8.ToArray()));

        var winHttp = new LoaderEntryFile("loader/winhttp.dll", BepInExLoader.WinHttp);
        byte[] dll = [0x4D, 0x5A, 0x90, 0x00];
        Assert.Same(dll, Loader.EntryFileContent(winHttp, dll));
        Assert.Equal(["loader/winhttp.dll", "loader/doorstop_config.ini"], Loader.EntryFiles.Select(file => file.SetPath));
    }

    [Fact]
    public void Mods_on_points_Doorstop_at_the_sets_preloader_and_mods_off_turns_it_off()
    {
        var set = Path.Combine("instances", "valheim-1", "mods", "sets", "7");

        Assert.Equal(
            ["--doorstop-enabled", "true", "--doorstop-target-assembly", Path.Combine(set, "BepInEx", "core", "BepInEx.Preloader.dll")],
            Loader.OnArguments(set));
        Assert.Equal(["--doorstop-enabled", "false"], Loader.OffArguments);
        Assert.Equal("BepInEx/config", Loader.ConfigFolder);
    }

    private static string[] Placements(string package, bool isLoaderPackage, string[] entries) =>
    [
        .. entries
            .Select(ModLayout.Normalize)
            .OfType<string>()
            .Select(entry => (Entry: entry, Target: Loader.Place(package, isLoaderPackage, entry)))
            .Where(placed => placed.Target is not null)
            .Select(placed => $"{placed.Entry} -> {placed.Target}"),
    ];
}
