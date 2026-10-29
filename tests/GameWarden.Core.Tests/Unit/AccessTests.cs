using System.Net;
using GameWarden.Core.Access;
using Microsoft.Extensions.Time.Testing;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class AccessTests
{
    private static readonly PanelNetwork Defaults = PanelNetwork.From(new GameWardenOptions());

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("26.12.34.56", true)] // Radmin VPN
    [InlineData("::ffff:26.12.34.56", true)] // the same on a dual-stack socket
    [InlineData("100.101.102.103", true)] // Tailscale
    [InlineData("192.168.1.20", false)] // the LAN needs the Owner's explicit setting
    [InlineData("203.0.113.7", false)]
    public void The_panel_answers_on_loopback_and_the_vpn_ranges_only(string local, bool accepted) =>
        Assert.Equal(accepted, Defaults.AcceptsLocal(IPAddress.Parse(local)));

    [Fact]
    public void Access_networks_replace_the_vpn_defaults_and_bad_entries_are_reported()
    {
        var network = PanelNetwork.From(new GameWardenOptions { PanelAccessNetworks = ["192.168.1.0/24", "not-a-range"] });

        Assert.True(network.AcceptsLocal(IPAddress.Parse("192.168.1.20")));
        Assert.False(network.AcceptsLocal(IPAddress.Parse("26.12.34.56")));
        Assert.True(network.AcceptsLocal(IPAddress.Loopback));
        Assert.Equal(["not-a-range"], network.Invalid);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("26.12.34.56", true)]
    [InlineData("[::1]", true)]
    [InlineData("vm.tail1234.ts.net", true)] // configured
    [InlineData("evil.example", false)] // DNS rebinding
    [InlineData("", false)]
    public void Host_headers_must_be_addresses_localhost_or_configured_names(string host, bool accepted)
    {
        var network = PanelNetwork.From(new GameWardenOptions { PanelHostNames = ["vm.tail1234.ts.net"] });

        Assert.Equal(accepted, network.AcceptsHost(host));
    }

    [Fact]
    public void Five_failures_lock_a_user_name_for_fifteen_minutes()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-28T10:00:00Z"));
        var throttle = new LoginThrottle(time);

        for (var i = 0; i < 4; i++)
        {
            throttle.Failed("owner");
            time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Null(throttle.LockedFor("owner"));
        throttle.Failed("Owner"); // names ignore case
        Assert.Equal(TimeSpan.FromMinutes(15), throttle.LockedFor("owner"));
        Assert.Null(throttle.LockedFor("someone-else"));

        time.Advance(TimeSpan.FromMinutes(15));
        Assert.Null(throttle.LockedFor("owner"));
    }

    [Fact]
    public void A_success_forgets_earlier_failures()
    {
        var throttle = new LoginThrottle(new FakeTimeProvider());
        for (var i = 0; i < 4; i++)
        {
            throttle.Failed("owner");
        }

        throttle.Succeeded("owner");
        throttle.Failed("owner");

        Assert.Null(throttle.LockedFor("owner"));
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("exactly10!", true)]
    [InlineData("correct horse battery staple", true)]
    public void Passwords_need_ten_characters_and_nothing_else(string password, bool accepted) =>
        Assert.Equal(accepted, Accounts.CheckPassword(password) is null);

    [Theory]
    [InlineData("owner", true)]
    [InlineData("vinh.nguyen_2", true)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void User_names_are_simple(string userName, bool accepted) =>
        Assert.Equal(accepted, Accounts.CheckUserName(userName) is null);
}
