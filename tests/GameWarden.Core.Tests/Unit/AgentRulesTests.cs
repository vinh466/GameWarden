using System.Net;
using GameWarden.Core.Agents;

namespace GameWarden.Core.Tests.Unit;

/// <summary>Pure Agent rules: the permission catalog, CIDR matching and window expiry (docs/15).</summary>
public sealed class AgentRulesTests
{
    [Fact]
    public void The_full_preset_contains_every_current_permission_and_observer_stays_read_only()
    {
        var full = AgentPermissions.Presets.Single(preset => preset.Key == "full");
        var observer = AgentPermissions.Presets.Single(preset => preset.Key == "observer");

        Assert.Equal(AgentPermissions.All.Select(permission => permission.Key).Order(), full.Permissions.Order());
        Assert.All(observer.Permissions, permission => Assert.Equal("observe", AgentPermissions.All.Single(p => p.Key == permission).Group));
        Assert.DoesNotContain(AgentPermissions.InstancesControl, observer.Permissions);
    }

    [Fact]
    public void An_empty_network_list_accepts_everything_and_a_cidr_only_its_range()
    {
        Assert.True(AgentNetwork.Accepts([], IPAddress.Parse("100.64.0.10")));
        Assert.True(AgentNetwork.Accepts(["100.64.0.0/24"], IPAddress.Parse("100.64.0.10")));
        Assert.False(AgentNetwork.Accepts(["100.64.0.0/24"], IPAddress.Parse("100.64.1.10")));
        Assert.True(AgentNetwork.Accepts(["100.64.0.0/24"], IPAddress.Parse("::ffff:100.64.0.10")));
    }

    [Fact]
    public void An_invalid_range_is_a_validation_error_naming_the_value()
    {
        var error = Assert.Throws<AgentRequestException>(() => AgentNetwork.Normalize(["100.64.0.0/99"], "allowedNetworks"));

        Assert.Equal("validation_failed", error.Code);
        Assert.Contains("100.64.0.0/99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mode_is_live_only_while_enabled_and_before_its_expiry()
    {
        var now = DateTimeOffset.Parse("2026-10-31T12:00:00Z");

        Assert.True(AgentSettingsService.IsLive(true, now.AddMinutes(1), now));
        Assert.False(AgentSettingsService.IsLive(true, now, now));
        Assert.False(AgentSettingsService.IsLive(false, now.AddMinutes(1), now));
        Assert.False(AgentSettingsService.IsLive(true, null, now));
    }
}
