using GameWarden.Core.Mods;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The resolver's rules (docs/13 §4.5) over dependency tables like Thunderstore's.</summary>
[Trait("Category", "Unit")]
public sealed class ModResolverTests
{
    private const string Pack = "denikson-BepInExPack_Valheim";

    /// <summary>The real dependencies of 2026-10-30 (docs/13 §3), plus a few made up for the rules.</summary>
    private static readonly Dictionary<string, string[]> Thunderstore = new(StringComparer.Ordinal)
    {
        ["RandyKnapp-EpicLoot-0.15.4"] = ["ValheimModding-JsonDotNET-13.0.4", "ValheimModding-Jotunn-2.30.2", $"{Pack}-5.4.2351"],
        ["ValheimModding-Jotunn-2.30.2"] = [$"{Pack}-5.4.2333"],
        ["ValheimModding-Jotunn-2.29.0"] = [$"{Pack}-5.4.2333"],
        ["ValheimModding-JsonDotNET-13.0.4"] = [],
        [$"{Pack}-5.4.2333"] = [],
        [$"{Pack}-5.4.2351"] = [],
        [$"{Pack}-5.4.2360"] = [],
        ["Tester-Lonely-1.0.0"] = [],
        ["Tester-Selfish-1.0.0"] = ["Tester-Selfish-0.9.0"],
        ["Tester-Chicken-1.0.0"] = ["Tester-Egg-1.0.0"],
        ["Tester-Egg-1.0.0"] = ["Tester-Chicken-1.0.0"],
        ["Tester-Broken-1.0.0"] = ["not a dependency"],
    };

    private static readonly Dictionary<string, string> Latest = new(StringComparer.Ordinal) { [Pack] = "5.4.2360" };

    private static Task<ModResolution> ResolveAsync(string? loaderPackage, params ModChoice[] chosen) =>
        ModResolver.ResolveAsync(
            chosen,
            loaderPackage,
            package => Task.FromResult<IReadOnlyList<string>>(Thunderstore[package.ToString()]),
            package => Task.FromResult(Latest[package]));

    private static string[] Listed(ModResolution resolution) =>
        [.. resolution.Mods.Select(mod => $"{mod.Package} {mod.Version} {mod.Origin}{(mod.Enabled ? string.Empty : " disabled")}")];

    [Fact]
    public async Task Missing_dependencies_are_added_at_the_highest_version_any_mod_names()
    {
        var resolution = await ResolveAsync(Pack, new ModChoice("RandyKnapp-EpicLoot", "0.15.4", true));

        Assert.Equal(
            [
                "RandyKnapp-EpicLoot 0.15.4 chosen",
                $"{Pack} 5.4.2351 dependency", // Epic Loot names 5.4.2351, Jötunn 5.4.2333
                "ValheimModding-Jotunn 2.30.2 dependency",
                "ValheimModding-JsonDotNET 13.0.4 dependency",
            ],
            Listed(resolution));
        Assert.Empty(resolution.Warnings);
    }

    [Fact]
    public async Task Chosen_versions_never_change_and_an_older_one_is_a_warning()
    {
        var resolution = await ResolveAsync(
            Pack,
            new ModChoice("RandyKnapp-EpicLoot", "0.15.4", true),
            new ModChoice("ValheimModding-Jotunn", "2.29.0", true),
            new ModChoice(Pack, "5.4.2333", true));

        Assert.Contains("ValheimModding-Jotunn 2.29.0 chosen", Listed(resolution));
        Assert.Contains($"{Pack} 5.4.2333 chosen", Listed(resolution));
        Assert.Contains($"{Pack} 5.4.2333 is older than the 5.4.2351 that RandyKnapp-EpicLoot-0.15.4 names.", resolution.Warnings);
        Assert.Contains("ValheimModding-Jotunn 2.29.0 is older than the 2.30.2 that RandyKnapp-EpicLoot-0.15.4 names.", resolution.Warnings);
    }

    [Fact]
    public async Task The_loader_package_is_added_at_its_latest_version_when_no_mod_names_it()
    {
        var resolution = await ResolveAsync(Pack, new ModChoice("Tester-Lonely", "1.0.0", true));

        Assert.Equal(["Tester-Lonely 1.0.0 chosen", $"{Pack} 5.4.2360 dependency"], Listed(resolution));
        Assert.Equal([$"{Pack} 5.4.2360 dependency"], Listed(await ResolveAsync(Pack)));
        Assert.Equal(["Tester-Lonely 1.0.0 chosen"], Listed(await ResolveAsync(null, new ModChoice("Tester-Lonely", "1.0.0", true))));
    }

    [Fact]
    public async Task Disabled_mods_stay_in_the_set_and_pull_in_nothing()
    {
        var resolution = await ResolveAsync(
            Pack,
            new ModChoice("RandyKnapp-EpicLoot", "0.15.4", false),
            new ModChoice("ValheimModding-Jotunn", "2.30.2", true));

        Assert.Equal(
            ["RandyKnapp-EpicLoot 0.15.4 chosen disabled", "ValheimModding-Jotunn 2.30.2 chosen", $"{Pack} 5.4.2333 dependency"],
            Listed(resolution));
    }

    [Fact]
    public async Task A_needed_mod_that_is_disabled_is_a_warning()
    {
        var resolution = await ResolveAsync(
            Pack,
            new ModChoice("RandyKnapp-EpicLoot", "0.15.4", true),
            new ModChoice("ValheimModding-JsonDotNET", "13.0.4", false));

        Assert.Contains("ValheimModding-JsonDotNET is disabled, but RandyKnapp-EpicLoot-0.15.4 needs it.", resolution.Warnings);
        Assert.DoesNotContain(resolution.Mods, mod => mod is { Package: "ValheimModding-JsonDotNET", Origin: ModResolver.Dependency });
    }

    [Fact]
    public async Task Cycles_and_unreadable_dependencies_are_refused()
    {
        var cycle = await Assert.ThrowsAsync<ModResolveException>(() => ResolveAsync(null, new ModChoice("Tester-Chicken", "1.0.0", true)));
        Assert.Contains("Tester-Chicken -> Tester-Egg -> Tester-Chicken", cycle.Message, StringComparison.Ordinal);

        var broken = await Assert.ThrowsAsync<ModResolveException>(() => ResolveAsync(null, new ModChoice("Tester-Broken", "1.0.0", true)));
        Assert.Contains("'not a dependency'", broken.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ModResolveException>(() =>
            ResolveAsync(null, new ModChoice("Tester-Lonely", "1.0.0", true), new ModChoice("Tester-Lonely", "1.0.0", true)));
    }

    [Fact]
    public async Task A_package_naming_itself_is_no_cycle()
    {
        var resolution = await ResolveAsync(null, new ModChoice("Tester-Selfish", "1.0.0", true));

        Assert.Equal(["Tester-Selfish 1.0.0 chosen"], Listed(resolution));
    }
}
