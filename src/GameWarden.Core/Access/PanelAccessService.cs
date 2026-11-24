using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Access;

/// <summary>
/// Resolves a panel user's permissions from the role bindings (docs/06 §5, ADR-0036). The Owner is an implicit allow;
/// everyone else gets the union of the bindings that cover the target instance. Results are computed per request, so
/// a role change takes effect without a new cookie.
/// </summary>
public sealed class PanelAccessService(IDbContextFactory<GameWardenDb> db)
{
    public async Task<IReadOnlyList<RoleBinding>> BindingsAsync(string userId, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.RoleBindings.AsNoTracking()
            .Where(binding => binding.UserId == userId)
            .OrderBy(binding => binding.Role).ThenBy(binding => binding.ScopeType).ThenBy(binding => binding.ScopeId)
            .Select(binding => new RoleBinding(binding.Role, binding.ScopeType, binding.ScopeId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The permissions on one instance, or the global permissions when <paramref name="instanceId"/> is null.</summary>
    public async Task<IReadOnlySet<string>> PermissionsAsync(UserRecord user, string? instanceId, CancellationToken cancellationToken)
    {
        if (user.IsOwner)
        {
            return PanelPermissions.All;
        }

        return PanelRoleCatalog.Effective(await BindingsAsync(user.Id, cancellationToken), instanceId);
    }

    public async Task<bool> HasAsync(UserRecord user, string permission, string? instanceId, CancellationToken cancellationToken)
    {
        if (user.IsOwner || !PanelPermissions.All.Contains(permission))
        {
            // An unknown permission is a programming error, not a grant: fail closed.
            return user.IsOwner;
        }

        return (await PermissionsAsync(user, instanceId, cancellationToken)).Contains(permission);
    }
}
