using GameWarden.Core.Access;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The database side of the role bindings (ADR-0036): a global binding, an instance binding and the Owner.</summary>
[Trait("Category", "Unit")]
public sealed class PanelAccessServiceTests
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Bindings_are_read_from_the_database_and_scoped_to_the_instance()
    {
        await using var harness = await CoreHarness.StartAsync();
        var accounts = harness.Get<Accounts>();
        var owner = (await accounts.CreateOwnerAsync("owner", Password, CancellationToken.None))!;
        var friend = (await accounts.CreateUserAsync("friend", Password, isOwner: false, cancellationToken: CancellationToken.None))!;

        await using (var db = await harness.Get<IDbContextFactory<GameWardenDb>>().CreateDbContextAsync(CancellationToken.None))
        {
            db.RoleBindings.Add(new RoleBindingRecord
            {
                UserId = friend.Id,
                Role = PanelRoles.Operator,
                ScopeType = PanelScopes.Instance,
                ScopeId = "valheim-main",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = owner.Id,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }

        var access = harness.Get<PanelAccessService>();
        var onValheim = await access.PermissionsAsync(friend, "valheim-main", CancellationToken.None);
        var onPalworld = await access.PermissionsAsync(friend, "palworld", CancellationToken.None);
        var global = await access.PermissionsAsync(friend, null, CancellationToken.None);

        Assert.Contains(PanelPermissions.InstanceControl, onValheim);
        Assert.Contains(PanelPermissions.InstancePlayersKick, onValheim);
        Assert.Empty(onPalworld);
        Assert.Empty(global);
        Assert.True(await access.HasAsync(friend, PanelPermissions.InstanceControl, "valheim-main", CancellationToken.None));
        Assert.False(await access.HasAsync(friend, PanelPermissions.InstanceControl, "palworld", CancellationToken.None));
        Assert.True(await access.HasAsync(owner, PanelPermissions.SystemManage, null, CancellationToken.None));
        Assert.Equal(PanelPermissions.All.Count, (await access.PermissionsAsync(owner, "anything", CancellationToken.None)).Count);
    }
}
