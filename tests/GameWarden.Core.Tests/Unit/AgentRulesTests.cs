using System.Net;
using GameWarden.Core.Agents;
using GameWarden.Core.Data;
using GameWarden.Core.Secrets;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public void A_short_secret_value_does_not_redact_every_occurrence_in_an_id()
    {
        var redactor = new AgentRedactor(new NoDatabase(), new FakeSecrets("test2"), TimeProvider.System);

        Assert.Equal("test2", redactor.Redact("test2"));
        Assert.Equal("-password ***", redactor.Redact("-password test2"));
    }

    private sealed class FakeSecrets(string value) : ISecretStore
    {
        public void Set(string scope, string name, string value) => throw new NotSupportedException();
        public string? Get(string scope, string name) => scope == SecretScopes.System && name == "password" ? value : null;
        public IReadOnlyList<string> Names(string scope) => scope == SecretScopes.System ? ["password"] : [];
        public bool Delete(string scope, string name) => throw new NotSupportedException();
        public void DeleteScope(string scope) => throw new NotSupportedException();
    }

    private sealed class NoDatabase : IDbContextFactory<GameWardenDb>
    {
        public GameWardenDb CreateDbContext() => throw new InvalidOperationException("No database in this test.");
        public Task<GameWardenDb> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No database in this test.");
    }
}
