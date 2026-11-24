using GameWarden.Core.Access;

namespace GameWarden.Core.Tests.Unit;

/// <summary>Owner and disabled-state guards (ADR-0036); the database is a fresh Core harness per test.</summary>
[Trait("Category", "Unit")]
public sealed class AccountsAccessTests
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task The_last_owner_cannot_be_demoted_or_disabled()
    {
        await using var harness = await CoreHarness.StartAsync();
        var accounts = harness.Get<Accounts>();
        var owner = (await accounts.CreateOwnerAsync("owner", Password, CancellationToken.None))!;

        var demoted = await accounts.SetOwnerAsync(owner.Id, false, CancellationToken.None);
        var disabled = await accounts.SetDisabledAsync(owner.Id, true, CancellationToken.None);

        Assert.Equal("last_owner", demoted.Code);
        Assert.Equal("last_owner", disabled.Code);
        Assert.True((await accounts.FindAsync(owner.Id, CancellationToken.None))!.IsOwner);
    }

    [Fact]
    public async Task A_second_owner_can_take_over_and_the_new_last_owner_is_protected()
    {
        await using var harness = await CoreHarness.StartAsync();
        var accounts = harness.Get<Accounts>();
        var first = (await accounts.CreateOwnerAsync("owner", Password, CancellationToken.None))!;
        var second = (await accounts.CreateUserAsync("friend", Password, isOwner: true, cancellationToken: CancellationToken.None))!;

        var demoted = await accounts.SetOwnerAsync(first.Id, false, CancellationToken.None);
        var last = await accounts.SetOwnerAsync(second.Id, false, CancellationToken.None);

        Assert.Null(demoted.Code);
        Assert.False((await accounts.FindAsync(first.Id, CancellationToken.None))!.IsOwner);
        Assert.Equal("last_owner", last.Code);
        Assert.True((await accounts.FindAsync(second.Id, CancellationToken.None))!.IsOwner);
    }

    [Fact]
    public async Task A_disabled_user_cannot_sign_in_until_enabled_again()
    {
        await using var harness = await CoreHarness.StartAsync();
        var accounts = harness.Get<Accounts>();
        _ = (await accounts.CreateOwnerAsync("owner", Password, CancellationToken.None))!;
        var friend = (await accounts.CreateUserAsync("friend", Password, isOwner: false, cancellationToken: CancellationToken.None))!;

        var disabled = await accounts.SetDisabledAsync(friend.Id, true, CancellationToken.None);
        var whileDisabled = await accounts.VerifyAsync("friend", Password, CancellationToken.None);
        var enabled = await accounts.SetDisabledAsync(friend.Id, false, CancellationToken.None);
        var afterEnabled = await accounts.VerifyAsync("friend", Password, CancellationToken.None);

        Assert.Null(disabled.Code);
        Assert.Null(whileDisabled);
        Assert.Null(enabled.Code);
        Assert.NotNull(afterEnabled);
        Assert.NotNull(afterEnabled.LastSeenAt);
    }
}
