using System.Collections.Frozen;

namespace GameWarden.Core.Access;

/// <summary>
/// The panel permission names (docs/06-security-and-access.md §5, ADR-0036). Code checks these strings; a role is a
/// named set in <see cref="PanelRoles"/>. Adding a role does not change an endpoint.
/// </summary>
public static class PanelPermissions
{
    public const string SystemRead = "system.read";
    public const string SystemManage = "system.manage";
    public const string UsersManage = "users.manage";
    public const string AuditRead = "audit.read";
    public const string AdaptersRead = "adapters.read";
    public const string AdaptersManage = "adapters.manage";
    public const string InstancesCreate = "instances.create";

    public const string InstanceView = "instance.view";
    public const string InstanceDetailsRead = "instance.details.read";
    public const string InstanceConnectInfo = "instance.connectInfo";
    public const string InstanceControl = "instance.control";
    public const string InstanceConsoleRead = "instance.console.read";
    public const string InstanceConsoleWrite = "instance.console.write";
    public const string InstanceConsoleWriteAny = "instance.console.writeAny";
    public const string InstanceSettingsRead = "instance.settings.read";
    public const string InstanceSettingsWrite = "instance.settings.write";
    public const string InstanceUpdate = "instance.update";
    public const string InstanceBackupCreate = "instance.backup.create";
    public const string InstanceBackupRestore = "instance.backup.restore";
    public const string InstanceModsManage = "instance.mods.manage";
    public const string InstanceScheduleManage = "instance.schedule.manage";
    public const string InstanceDelete = "instance.delete";
    public const string InstanceAccessManage = "instance.access.manage";
    public const string InstancePlayersRead = "instance.players.read";
    public const string InstancePlayersKick = "instance.players.kick";
    public const string InstancePlayersBan = "instance.players.ban";
    public const string InstanceAdminsManage = "instance.admins.manage";
    public const string InstanceAnnounce = "instance.announce";
    public const string InstanceSave = "instance.save";
    public const string InstanceRestartRequest = "instance.restart.request";

    /// <summary>Every permission the panel knows. The Owner holds this set implicitly.</summary>
    public static readonly FrozenSet<string> All = new[]
    {
        SystemRead, SystemManage, UsersManage, AuditRead, AdaptersRead, AdaptersManage, InstancesCreate,
        InstanceView, InstanceDetailsRead, InstanceConnectInfo, InstanceControl, InstanceConsoleRead,
        InstanceConsoleWrite, InstanceConsoleWriteAny, InstanceSettingsRead, InstanceSettingsWrite, InstanceUpdate,
        InstanceBackupCreate, InstanceBackupRestore, InstanceModsManage, InstanceScheduleManage, InstanceDelete,
        InstanceAccessManage, InstancePlayersRead, InstancePlayersKick, InstancePlayersBan, InstanceAdminsManage,
        InstanceAnnounce, InstanceSave, InstanceRestartRequest,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Permissions that apply to one instance; the policy resolves the id and the binding scope.</summary>
    public static bool IsInstanceScoped(string permission) => permission.StartsWith("instance.", StringComparison.Ordinal);
}

/// <summary>The three fixed friend roles (ADR-0006, ADR-0036). Owner is a flag on the user, not a role.</summary>
public static class PanelRoles
{
    public const string Admin = "admin";
    public const string Operator = "operator";
    public const string Player = "player";

    public static readonly FrozenSet<string> All = new[] { Admin, Operator, Player }.ToFrozenSet(StringComparer.Ordinal);
}

/// <summary>A role binding's scope: global covers every server, current and future.</summary>
public static class PanelScopes
{
    public const string Global = "global";
    public const string Instance = "instance";
}

/// <summary>
/// One role binding, as the database stores it. <see cref="ScopeId"/> is the instance id, or empty for a global
/// binding (empty rather than null keeps the unique index meaningful in SQLite).
/// </summary>
public sealed record RoleBinding(string Role, string ScopeType, string ScopeId)
{
    public bool Covers(string? instanceId) =>
        ScopeType == PanelScopes.Global || (!string.IsNullOrEmpty(instanceId) && ScopeType == PanelScopes.Instance && ScopeId == instanceId);
}

/// <summary>
/// The role matrix in one place (docs/23 §5.2, ADR-0036). Endpoints never check a role name; this table turns a role
/// into permissions. A binding contributes its role's set when it covers the target instance.
/// </summary>
public static class PanelRoleCatalog
{
    private static readonly FrozenSet<string> AdminPermissions = new[]
    {
        PanelPermissions.AdaptersRead,
        PanelPermissions.InstanceView, PanelPermissions.InstanceDetailsRead, PanelPermissions.InstanceConnectInfo,
        PanelPermissions.InstanceControl, PanelPermissions.InstanceConsoleRead, PanelPermissions.InstanceConsoleWrite,
        PanelPermissions.InstanceConsoleWriteAny, PanelPermissions.InstanceSettingsRead,
        PanelPermissions.InstanceSettingsWrite, PanelPermissions.InstanceUpdate,
        PanelPermissions.InstanceBackupCreate, PanelPermissions.InstanceBackupRestore,
        PanelPermissions.InstanceModsManage, PanelPermissions.InstanceScheduleManage,
        PanelPermissions.InstancePlayersRead, PanelPermissions.InstancePlayersKick, PanelPermissions.InstancePlayersBan,
        PanelPermissions.InstanceAdminsManage, PanelPermissions.InstanceAnnounce, PanelPermissions.InstanceSave,
        PanelPermissions.InstanceRestartRequest,
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> OperatorPermissions = new[]
    {
        PanelPermissions.AdaptersRead,
        PanelPermissions.InstanceView, PanelPermissions.InstanceDetailsRead, PanelPermissions.InstanceConnectInfo,
        PanelPermissions.InstanceControl, PanelPermissions.InstanceConsoleRead, PanelPermissions.InstanceConsoleWrite,
        PanelPermissions.InstanceSettingsRead, PanelPermissions.InstanceBackupCreate,
        PanelPermissions.InstancePlayersRead, PanelPermissions.InstancePlayersKick,
        PanelPermissions.InstanceAnnounce, PanelPermissions.InstanceSave, PanelPermissions.InstanceRestartRequest,
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> PlayerPermissions = new[]
    {
        PanelPermissions.InstanceView, PanelPermissions.InstanceConnectInfo,
        PanelPermissions.InstancePlayersRead, PanelPermissions.InstanceRestartRequest,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The permissions of one role; empty for an unknown name, which the API treats as no access.</summary>
    public static IReadOnlySet<string> ForRole(string role) => role switch
    {
        PanelRoles.Admin => AdminPermissions,
        PanelRoles.Operator => OperatorPermissions,
        PanelRoles.Player => PlayerPermissions,
        _ => FrozenSet<string>.Empty,
    };

    /// <summary>
    /// The union of every binding that covers <paramref name="instanceId"/>, or only global bindings when it is null.
    /// The Owner is handled by the caller (it holds every permission).
    /// </summary>
    public static IReadOnlySet<string> Effective(IEnumerable<RoleBinding> bindings, string? instanceId)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (instanceId is null)
            {
                // No target instance: this is the global surface (system, users, adapters), not "all servers".
                if (binding.ScopeType == PanelScopes.Global)
                {
                    permissions.UnionWith(ForRole(binding.Role).Where(permission => !PanelPermissions.IsInstanceScoped(permission)));
                }

                continue;
            }

            if (binding.Covers(instanceId))
            {
                permissions.UnionWith(ForRole(binding.Role));
            }
        }

        return permissions;
    }
}
