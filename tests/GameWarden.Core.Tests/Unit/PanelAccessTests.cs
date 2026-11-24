using GameWarden.Core.Access;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The role matrix and the binding rules (docs/23 §5.2–5.3, ADR-0036).</summary>
[Trait("Category", "Unit")]
public sealed class PanelAccessTests
{
    [Fact]
    public void The_catalog_has_every_permission_once_and_the_instance_scopes_are_marked()
    {
        var permissions = typeof(PanelPermissions).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.All(permissions, permission => Assert.Contains(permission, (IReadOnlySet<string>)PanelPermissions.All));
        Assert.Equal(permissions.Length, PanelPermissions.All.Count);
        Assert.All(PanelPermissions.All, permission =>
            Assert.Equal(permission.StartsWith("instance.", StringComparison.Ordinal), PanelPermissions.IsInstanceScoped(permission)));
    }

    [Fact]
    public void Admin_runs_a_server_but_has_no_system_user_or_server_lifecycle_rights()
    {
        var admin = PanelRoleCatalog.ForRole(PanelRoles.Admin);

        Assert.Contains(PanelPermissions.InstanceControl, admin);
        Assert.Contains(PanelPermissions.InstanceSettingsWrite, admin);
        Assert.Contains(PanelPermissions.InstanceModsManage, admin);
        Assert.Contains(PanelPermissions.InstancePlayersBan, admin);
        Assert.Contains(PanelPermissions.InstanceAdminsManage, admin);
        Assert.Contains(PanelPermissions.InstanceAnnounce, admin);
        Assert.Contains(PanelPermissions.AdaptersRead, admin);

        Assert.DoesNotContain(PanelPermissions.InstancesCreate, admin);
        Assert.DoesNotContain(PanelPermissions.InstanceDelete, admin);
        Assert.DoesNotContain(PanelPermissions.InstanceAccessManage, admin);
        Assert.DoesNotContain(PanelPermissions.SystemRead, admin);
        Assert.DoesNotContain(PanelPermissions.UsersManage, admin);
        Assert.DoesNotContain(PanelPermissions.AdaptersManage, admin);
    }

    [Fact]
    public void Operator_keeps_the_server_running_without_changing_it()
    {
        var op = PanelRoleCatalog.ForRole(PanelRoles.Operator);

        Assert.Contains(PanelPermissions.InstanceControl, op);
        Assert.Contains(PanelPermissions.InstanceConsoleWrite, op);
        Assert.Contains(PanelPermissions.InstanceSettingsRead, op);
        Assert.Contains(PanelPermissions.InstanceBackupCreate, op);
        Assert.Contains(PanelPermissions.InstancePlayersKick, op);

        Assert.DoesNotContain(PanelPermissions.InstanceSettingsWrite, op);
        Assert.DoesNotContain(PanelPermissions.InstanceModsManage, op);
        Assert.DoesNotContain(PanelPermissions.InstanceUpdate, op);
        Assert.DoesNotContain(PanelPermissions.InstanceDelete, op);
        Assert.DoesNotContain(PanelPermissions.InstancePlayersBan, op);
        Assert.DoesNotContain(PanelPermissions.SystemRead, op);
    }

    [Fact]
    public void Player_sees_the_server_and_can_ask_for_a_restart()
    {
        var player = PanelRoleCatalog.ForRole(PanelRoles.Player);

        Assert.Contains(PanelPermissions.InstanceView, player);
        Assert.Contains(PanelPermissions.InstanceConnectInfo, player);
        Assert.Contains(PanelPermissions.InstancePlayersRead, player);
        Assert.Contains(PanelPermissions.InstanceRestartRequest, player);

        Assert.DoesNotContain(PanelPermissions.InstanceControl, player);
        Assert.DoesNotContain(PanelPermissions.InstanceDetailsRead, player);
        Assert.DoesNotContain(PanelPermissions.InstanceConsoleRead, player);
        Assert.DoesNotContain(PanelPermissions.InstanceSettingsRead, player);
    }

    [Fact]
    public void An_unknown_role_grants_nothing()
    {
        Assert.Empty(PanelRoleCatalog.ForRole("owner"));
        Assert.Empty(PanelRoleCatalog.ForRole(""));
    }

    [Fact]
    public void A_global_binding_covers_every_instance_and_the_global_scope()
    {
        var binding = new RoleBinding(PanelRoles.Player, PanelScopes.Global, "");
        var onOne = PanelRoleCatalog.Effective([binding], "valheim-main");
        var global = PanelRoleCatalog.Effective([binding], null);

        Assert.Contains(PanelPermissions.InstanceView, onOne);
        Assert.Contains(PanelPermissions.InstanceConnectInfo, onOne);
        Assert.Empty(global); // a global Player grant is not a system permission
    }

    [Fact]
    public void An_instance_binding_does_not_leak_to_another_instance_or_to_the_global_scope()
    {
        var binding = new RoleBinding(PanelRoles.Operator, PanelScopes.Instance, "valheim-main");

        Assert.Contains(PanelPermissions.InstanceControl, PanelRoleCatalog.Effective([binding], "valheim-main"));
        Assert.Empty(PanelRoleCatalog.Effective([binding], "palworld"));
        Assert.Empty(PanelRoleCatalog.Effective([binding], null));
    }

    [Fact]
    public void Bindings_are_a_union_so_a_friend_can_play_everywhere_and_operate_one_server()
    {
        var bindings = new[]
        {
            new RoleBinding(PanelRoles.Player, PanelScopes.Global, ""),
            new RoleBinding(PanelRoles.Operator, PanelScopes.Instance, "valheim-main"),
        };

        var onValheim = PanelRoleCatalog.Effective(bindings, "valheim-main");
        var onPalworld = PanelRoleCatalog.Effective(bindings, "palworld");

        Assert.Contains(PanelPermissions.InstanceControl, onValheim);
        Assert.Contains(PanelPermissions.InstanceConnectInfo, onValheim);
        Assert.Contains(PanelPermissions.InstanceConnectInfo, onPalworld);
        Assert.DoesNotContain(PanelPermissions.InstanceControl, onPalworld);
    }
}
