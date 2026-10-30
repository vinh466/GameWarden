using System.IO.Compression;
using System.Text;
using GameWarden.Core.Mods;
using Microsoft.Extensions.Time.Testing;

namespace GameWarden.Core.Tests.Unit;

/// <summary>
/// r2modman profile codes both ways (docs/13 §3, §4.4, §4.6): a code shaped exactly like r2modman's export is read, the
/// share code is written in the shape r2modman reads, and codes that are not r2modman's, too large or that leave their
/// folder are refused.
/// </summary>
[Trait("Category", "Unit")]
public sealed class R2ModmanCodeTests
{
    private const string Loader = FakeThunderstore.Loader;

    [Fact]
    public void A_code_shaped_like_r2modmans_export_gives_its_mods_in_order_and_its_config_files()
    {
        var yaml = R2ModmanFixture.ExportYaml(
            "Friends",
            (Loader, "5.4.2333", true),
            ("ValheimModding-Jotunn", "2.30.2", true),
            ("RandyKnapp-EpicLoot", "0.15.4", null),
            ("ValheimModding-JsonDotNET", "13.0.4", false));
        var text = R2ModmanFixture.Text(
            yaml,
            ("config/BepInEx.cfg", "[Logging]"),
            ("config/randyknapp.mods.epicloot.cfg", "[General]\nfrom the code"),
            ("config\\EpicLoot\\patches\\extra.json", "{}"),
            ("config/", string.Empty),
            ("mods.yml", "- not read"),
            ("doorstop_config.ini", "[General]"),
            ("BepInEx/plugins/RandyKnapp-EpicLoot/translations.json", "{}"),
            ("config/evil.dll", "MZ"));

        var profile = R2ModmanCode.Read(text);

        Assert.Equal("Friends", profile.ProfileName);
        Assert.Equal(
            [
                new R2ModmanMod(Loader, "5.4.2333", true),
                new R2ModmanMod("ValheimModding-Jotunn", "2.30.2", true),
                new R2ModmanMod("RandyKnapp-EpicLoot", "0.15.4", true), // no `enabled`: enabled, as r2modman reads it
                new R2ModmanMod("ValheimModding-JsonDotNET", "13.0.4", false),
            ],
            profile.Mods);
        Assert.Equal(
            ["BepInEx.cfg", "EpicLoot/patches/extra.json", "randyknapp.mods.epicloot.cfg"],
            profile.ConfigFiles.Select(file => file.Path).ToArray());
        Assert.Equal("[General]\nfrom the code", Encoding.UTF8.GetString(profile.ConfigFiles.Single(file => file.Path == "randyknapp.mods.epicloot.cfg").Content));
        Assert.Collection(
            profile.Warnings,
            warning => Assert.Equal("Left out 1 file(s) that r2modman does not import either (programs and scripts): config/evil.dll.", warning),
            warning => Assert.Equal(
                "Left out 2 file(s) outside config/; only the profile's BepInEx config folder is used: doorstop_config.ini, BepInEx/plugins/RandyKnapp-EpicLoot/translations.json.",
                warning));
    }

    [Fact]
    public void Mods_that_are_not_Thunderstore_packages_or_lack_a_version_are_left_out_with_a_warning()
    {
        var yaml = R2ModmanFixture.ExportYaml("Odd", ("ValheimModding-Jotunn", "2.30.2", true), ("JustAName", "1.0.0", true), ("ValheimModding-Jotunn", "2.30.1", true))
            + "  - name: Tester-NoVersion\n    enabled: true\n"
            + "  - name: Tester-BadVersion\n    version:\n      major: 1\n      minor: x\n      patch: 0\n"
            + "  - version:\n      major: 1\n      minor: 0\n      patch: 0\n";

        var profile = R2ModmanCode.Read(R2ModmanFixture.Text(yaml));

        Assert.Equal([new R2ModmanMod("ValheimModding-Jotunn", "2.30.2", true)], profile.Mods);
        Assert.Equal(
            [
                "'JustAName' is not a Thunderstore package (Namespace-Name); it is left out.",
                "ValheimModding-Jotunn is in the code twice; the first one is kept.",
                "Tester-NoVersion has no version Core can read (major, minor, patch); it is left out.",
                "Tester-BadVersion has no version Core can read (major, minor, patch); it is left out.",
                "Mod 6 of the code has no name; it is left out.",
            ],
            profile.Warnings);
        Assert.Empty(profile.ConfigFiles);
    }

    [Fact]
    public void The_share_code_is_the_zip_r2modman_reads_with_export_r2x_only()
    {
        var text = R2ModmanCode.Write("Valheim: main", [new(Loader, "5.4.2351", true), new("RandyKnapp-EpicLoot", "0.15.4", true)]);

        var entries = R2ModmanFixture.Entries(text);
        Assert.Equal([R2ModmanCode.ExportFile], entries.Keys.ToArray());
        // js-yaml's output for r2modman's ExportFormat; the name is quoted because of its colon.
        Assert.Equal(
            "profileName: 'Valheim: main'\n"
            + "mods:\n"
            + "  - name: denikson-BepInExPack_Valheim\n    version:\n      major: 5\n      minor: 4\n      patch: 2351\n    enabled: true\n"
            + "  - name: RandyKnapp-EpicLoot\n    version:\n      major: 0\n      minor: 15\n      patch: 4\n    enabled: true\n",
            entries[R2ModmanCode.ExportFile]);
        var back = R2ModmanCode.Read(text);
        Assert.Equal("Valheim: main", back.ProfileName);
        Assert.Equal([new R2ModmanMod(Loader, "5.4.2351", true), new R2ModmanMod("RandyKnapp-EpicLoot", "0.15.4", true)], back.Mods);
        Assert.Empty(back.ConfigFiles);
        Assert.Empty(back.Warnings);
    }

    [Theory]
    [InlineData("config/../../escaped.cfg", "leaves its folder")]
    [InlineData("../escaped.cfg", "leaves its folder")]
    [InlineData("/etc/escaped.cfg", "is an absolute path")]
    [InlineData("C:/Windows/escaped.cfg", "names a drive or a stream")]
    public void An_entry_that_leaves_its_folder_refuses_the_code(string entry, string why)
    {
        var text = R2ModmanFixture.Text(R2ModmanFixture.ExportYaml("Slippery", ("ValheimModding-Jotunn", "2.30.2", true)), (entry, "x"));

        var error = Assert.Throws<R2ModmanCodeException>(() => R2ModmanCode.Read(text));

        Assert.Contains($"refused entry '{entry}'", error.Message, StringComparison.Ordinal);
        Assert.Contains(why, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_path_too_long_where_it_goes_refuses_the_code()
    {
        var text = R2ModmanFixture.Text(R2ModmanFixture.ExportYaml("Long", ("ValheimModding-Jotunn", "2.30.2", true)), ("config/" + new string('a', 300) + ".cfg", "x"));
        var root = Path.Combine(Path.GetTempPath(), "code-config");

        var error = Assert.Throws<R2ModmanCodeException>(() => R2ModmanCode.Read(text, path => ModLayout.Resolve(root, path)));

        Assert.Contains("the limit is 259", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Texts_that_are_not_r2modman_profiles_are_refused()
    {
        var yaml = R2ModmanFixture.ExportYaml("Fine", ("ValheimModding-Jotunn", "2.30.2", true));

        Assert.Contains("does not start with '#r2modman'", Refused(Convert.ToBase64String(R2ModmanFixture.ZipBytes(("export.r2x", yaml)))), StringComparison.Ordinal);
        Assert.Contains("is not base64", Refused("#r2modman\nnot base64!"), StringComparison.Ordinal);
        Assert.Contains("is not a zip", Refused("#r2modman\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes("plain text"))), StringComparison.Ordinal);
        Assert.Contains("has no export.r2x", Refused("#r2modman\n" + Convert.ToBase64String(R2ModmanFixture.ZipBytes(("config/a.cfg", "x")))), StringComparison.Ordinal);
        Assert.Contains("has no profileName", Refused(R2ModmanFixture.Text("mods: []\n")), StringComparison.Ordinal);
        Assert.Contains("has no list of mods", Refused(R2ModmanFixture.Text("profileName: x\nmods: none\n")), StringComparison.Ordinal);
        Assert.Contains("is not YAML", Refused(R2ModmanFixture.Text("profileName: [unclosed\n")), StringComparison.Ordinal);
        Assert.Contains("nests deeper than 16", Refused(R2ModmanFixture.Text("profileName: x\nmods: " + new string('[', 100) + new string(']', 100) + "\n")), StringComparison.Ordinal);

        // A code's text from r2modman's Windows build: CRLF after the prefix and a wrapped base64 still read.
        var wrapped = "#r2modman\r\n" + string.Join("\r\n", Convert.ToBase64String(R2ModmanFixture.ZipBytes(("export.r2x", yaml))).Chunk(76).Select(chunk => new string(chunk)));
        Assert.Equal("Fine", R2ModmanCode.Read(wrapped).ProfileName);
    }

    [Fact]
    public void Codes_over_the_limits_are_refused()
    {
        var yaml = R2ModmanFixture.ExportYaml("Big", ("ValheimModding-Jotunn", "2.30.2", true));

        Assert.Contains("a code holds at most 20000000", Refused("#r2modman\n" + Convert.ToBase64String(new byte[R2ModmanCode.MaxZipBytes + 1])), StringComparison.Ordinal);
        Assert.Contains(
            "is larger than 4194304 bytes",
            Refused(R2ModmanFixture.Text(yaml, ("config/huge.cfg", new string('x', (int)R2ModmanCode.MaxConfigFileBytes + 1)))),
            StringComparison.Ordinal);
        var many = Enumerable.Range(0, R2ModmanCode.MaxEntries).Select(index => ($"config/{index}.cfg", "x")).ToArray();
        Assert.Contains("entries; a code may have 5000", Refused(R2ModmanFixture.Text(yaml, many)), StringComparison.Ordinal);
        var mods = Enumerable.Range(0, R2ModmanCode.MaxMods + 1).Select(index => ($"Tester-Mod{index}", "1.0.0", (bool?)true)).ToArray();
        Assert.Contains("Core takes at most 2000", Refused(R2ModmanFixture.Text(R2ModmanFixture.ExportYaml("Many", mods))), StringComparison.Ordinal);
    }

    [Fact]
    public void A_zip_entry_larger_than_its_header_says_is_caught_while_reading()
    {
        // The header claims 1 byte; the data is 5 MB of the same byte, so it compresses well.
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(R2ModmanCode.ExportFile).Open()))
            {
                writer.Write(R2ModmanFixture.ExportYaml("Liar", ("ValheimModding-Jotunn", "2.30.2", true)));
            }

            using var stream = zip.CreateEntry("config/liar.cfg").Open();
            stream.Write(new byte[5 * 1024 * 1024]);
        }

        Assert.Contains("is larger than 4194304 bytes", Refused("#r2modman\n" + Convert.ToBase64String(memory.ToArray())), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13", "0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13")]
    [InlineData("  0192F1C4-7A51-7C3E-9D0B-5E2F8A6B4C13\n", "0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13")]
    [InlineData("0192f1c47a517c3e9d0b5e2f8a6b4c13", null)]
    [InlineData("{0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13}", null)]
    [InlineData("0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c1/", null)]
    [InlineData("../../api/experimental/package", null)]
    [InlineData("", null)]
    public void A_code_is_a_uuid_as_thunderstore_gives_it(string text, string? expected)
    {
        Assert.Equal(expected is not null, R2ModmanCode.TryNormalizeCode(text, out var code));
        Assert.Equal(expected, code);
    }

    [Fact]
    public void A_preview_lasts_thirty_minutes_and_belongs_to_its_instance()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-30T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var previews = new ModCodePreviews(time);
        var profile = new R2ModmanProfile("Friends", [new R2ModmanMod("ValheimModding-Jotunn", "2.30.2", true)], [], []);

        var preview = previews.Add("valheim-main", "0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13", profile);

        Assert.Matches("^[0-9a-f]{32}$", preview.Id);
        Assert.Equal(time.GetUtcNow() + TimeSpan.FromMinutes(30), preview.ExpiresAt);
        Assert.Same(preview, previews.Find("valheim-main", preview.Id));
        Assert.Null(previews.Find("other", preview.Id));
        Assert.Null(previews.Find("valheim-main", (preview.Id[0] == '0' ? "1" : "0") + preview.Id[1..])); // another id, whatever the first digit
        time.Advance(TimeSpan.FromMinutes(29));
        Assert.NotNull(previews.Find("valheim-main", preview.Id));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(previews.Find("valheim-main", preview.Id));

        // At most twenty are kept; the oldest goes first.
        var kept = Enumerable.Range(0, ModCodePreviews.MaxPreviews + 1).Select(index =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return previews.Add("valheim-main", "0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13", profile);
        }).ToList();
        Assert.Null(previews.Find("valheim-main", kept[0].Id));
        Assert.All(kept.Skip(1), later => Assert.NotNull(previews.Find("valheim-main", later.Id)));
    }

    private static string Refused(string text) => Assert.Throws<R2ModmanCodeException>(() => R2ModmanCode.Read(text)).Message;
}
