namespace GameWarden.Core.Data;

/// <summary>A panel account (docs/06-security-and-access.md §4). The MVP has one: the Owner, created by first-run setup.</summary>
public sealed class UserRecord
{
    public required string Id { get; set; }

    public required string UserName { get; set; }

    /// <summary>What the activity feed shows; the user name until the Owner or the user changes it.</summary>
    public string? DisplayName { get; set; }

    /// <summary>ASP.NET Core Identity <c>PasswordHasher</c> (PBKDF2) output.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Changes with the password; sessions carrying an older stamp end (sign-out everywhere).</summary>
    public required string SecurityStamp { get; set; }

    public bool IsOwner { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset PasswordChangedAt { get; set; }

    /// <summary>Set when the Owner disables the account; every session ends on its next request.</summary>
    public DateTimeOffset? DisabledAt { get; set; }

    /// <summary>Last successful sign-in, for the Users & Access list. Null before the first sign-in.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}
