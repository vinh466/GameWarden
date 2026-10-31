using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests;

/// <summary>Creating and editing a real Palworld instance: the generated admin password and the stored settings.</summary>
[Trait("Category", "Integration")]
public sealed class PalworldCreateTests
{
    [Fact]
    public async Task Creating_generates_the_admin_password_and_stores_the_wizard_settings()
    {
        await using var core = await CoreHarness.StartAsync();

        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "pal-1",
                "palworld",
                "Palworld",
                new Dictionary<string, string>
                {
                    ["ServerName"] = "GameWarden pals",
                    ["ServerPlayerMaxNum"] = "16",
                    ["Difficulty"] = "Normal",
                }),
            TestContext.Current.CancellationToken);
        Assert.NotNull(created.Instance);
        Assert.Equal(ActualState.NotInstalled, created.Instance.State);

        var view = await core.Instance("pal-1").GetSettingsAsync();

        Assert.True(view.Secrets["AdminPassword"].Set);
        Assert.Equal("GameWarden pals", view.Settings["ServerName"]);
        Assert.Equal("16", view.Settings["ServerPlayerMaxNum"]);
        Assert.Equal("Normal", view.Settings["Difficulty"]);
        Assert.DoesNotContain("ServerPassword", view.Secrets.Keys);
    }

    [Fact]
    public async Task Settings_of_every_type_round_trip()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.Supervisor.CreateAsync(
            new CreateInstanceRequest("pal-2", "palworld", "Palworld"),
            TestContext.Current.CancellationToken);

        var updated = await core.Instance("pal-2").UpdateSettingsAsync(new Dictionary<string, string?>
        {
            ["ExpRate"] = "2.5",
            ["CrossplayPlatforms"] = "Steam,Xbox",
            ["DenyTechnologyList"] = "PALBOX,RepairBench",
            ["bIsPvP"] = "true",
        });

        Assert.Equal("2.5", updated.Settings["ExpRate"]);
        Assert.Equal("Steam,Xbox", updated.Settings["CrossplayPlatforms"]);
        Assert.Equal("PALBOX,RepairBench", updated.Settings["DenyTechnologyList"]);
        Assert.Equal("true", updated.Settings["bIsPvP"]);
    }
}
