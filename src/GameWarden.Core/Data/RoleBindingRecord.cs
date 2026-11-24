namespace GameWarden.Core.Data;

/// <summary>
/// One role binding (docs/06 §5, ADR-0036). A user can have several; the effective permissions are the union of the
/// bindings that cover the target. <see cref="ScopeId"/> is the instance id, or empty for a global binding.
/// </summary>
public sealed class RoleBindingRecord
{
    public long Id { get; set; }

    public required string UserId { get; set; }

    /// <summary><c>admin</c>, <c>operator</c> or <c>player</c>.</summary>
    public required string Role { get; set; }

    /// <summary><c>global</c> or <c>instance</c>.</summary>
    public required string ScopeType { get; set; }

    /// <summary>The instance id when <see cref="ScopeType"/> is <c>instance</c>; empty for <c>global</c>.</summary>
    public required string ScopeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }
}
