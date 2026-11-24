using System.Security.Cryptography;
using GameWarden.Core.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Access;

/// <summary>Panel accounts (docs/06-security-and-access.md §4). The MVP has one, the Owner, created by first-run setup.</summary>
public sealed class Accounts(IDbContextFactory<GameWardenDb> db, TimeProvider time)
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 256;

    private static readonly PasswordHasher<UserRecord> Hasher = new();

    // Verifying against this when the user name is unknown keeps the answer time the same as for a wrong password.
    private static readonly string DummyHash = Hasher.HashPassword(null!, "not a password");

    private readonly SemaphoreSlim _create = new(1, 1);
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public async Task<bool> NeedsSetupAsync(CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return !await context.Users.AnyAsync(cancellationToken);
    }

    public async Task<UserRecord?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public async Task<UserRecord?> FindOwnerAsync(CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.Users.AsNoTracking().FirstOrDefaultAsync(user => user.IsOwner, cancellationToken);
    }

    /// <summary>The user when <paramref name="password"/> is theirs; null otherwise. The user name ignores case.</summary>
    public async Task<UserRecord?> VerifyAsync(string userName, string password, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.UserName == userName, cancellationToken);
        if (user is null)
        {
            Hasher.VerifyHashedPassword(null!, DummyHash, password);
            return null;
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
        }

        if (user.DisabledAt is not null)
        {
            // Verify the password first so a disabled account answers like a wrong password, then refuse it.
            return null;
        }

        user.LastSeenAt = time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return user;
    }

    /// <summary>First-run setup: creates the Owner, or returns null when an account exists already.</summary>
    public async Task<UserRecord?> CreateOwnerAsync(string userName, string password, CancellationToken cancellationToken)
    {
        await _create.WaitAsync(cancellationToken);
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            if (await context.Users.AnyAsync(cancellationToken))
            {
                return null;
            }

            var now = time.GetUtcNow();
            var owner = new UserRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = userName,
                DisplayName = userName,
                PasswordHash = string.Empty,
                SecurityStamp = NewStamp(),
                IsOwner = true,
                CreatedAt = now,
                PasswordChangedAt = now,
                LastSeenAt = now,
            };
            owner.PasswordHash = Hasher.HashPassword(owner, password);
            context.Users.Add(owner);
            await context.SaveChangesAsync(cancellationToken);
            return owner;
        }
        finally
        {
            _create.Release();
        }
    }

    /// <summary>Sets a new password and a new security stamp, which ends every session of this user.</summary>
    public async Task<UserRecord?> SetPasswordAsync(string id, string password, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.PasswordHash = Hasher.HashPassword(user, password);
        user.SecurityStamp = NewStamp();
        user.PasswordChangedAt = time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return user;
    }

    /// <summary>
    /// Creates a non-Owner account for an accepted invite (ADR-0036) or a test. The caller validates the user name and
    /// password; a duplicate name returns null. <paramref name="isOwner"/> is for an explicit promotion, never an
    /// invite.
    /// </summary>
    public async Task<UserRecord?> CreateUserAsync(string userName, string password, bool isOwner, CancellationToken cancellationToken)
    {
        await _create.WaitAsync(cancellationToken);
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            if (await context.Users.AnyAsync(candidate => candidate.UserName == userName, cancellationToken))
            {
                return null;
            }

            var now = time.GetUtcNow();
            var user = new UserRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = userName,
                DisplayName = userName,
                PasswordHash = string.Empty,
                SecurityStamp = NewStamp(),
                IsOwner = isOwner,
                CreatedAt = now,
                PasswordChangedAt = now,
            };
            user.PasswordHash = Hasher.HashPassword(user, password);
            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);
            return user;
        }
        finally
        {
            _create.Release();
        }
    }

    /// <summary>
    /// Promotes or demotes a user (ADR-0036). The last Owner cannot be demoted. The security stamp changes, so the
    /// affected sessions end and the cookie's Owner claim cannot go stale.
    /// </summary>
    public async Task<AccountChange> SetOwnerAsync(string id, bool isOwner, CancellationToken cancellationToken)
    {
        await _mutate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (user is null)
            {
                return new AccountChange(null, "not_found");
            }

            if (!isOwner && user.IsOwner && await context.Users.CountAsync(candidate => candidate.IsOwner, cancellationToken) <= 1)
            {
                return new AccountChange(user, "last_owner");
            }

            if (user.IsOwner != isOwner)
            {
                user.IsOwner = isOwner;
                user.SecurityStamp = NewStamp();
                await context.SaveChangesAsync(cancellationToken);
            }

            return new AccountChange(user, null);
        }
        finally
        {
            _mutate.Release();
        }
    }

    /// <summary>Disables or re-enables a user; the last Owner cannot be disabled. Both directions end every session.</summary>
    public async Task<AccountChange> SetDisabledAsync(string id, bool disabled, CancellationToken cancellationToken)
    {
        await _mutate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (user is null)
            {
                return new AccountChange(null, "not_found");
            }

            if (disabled && user.IsOwner && await context.Users.CountAsync(candidate => candidate.IsOwner, cancellationToken) <= 1)
            {
                return new AccountChange(user, "last_owner");
            }

            if ((user.DisabledAt is not null) != disabled)
            {
                user.DisabledAt = disabled ? time.GetUtcNow() : null;
                user.SecurityStamp = NewStamp();
                await context.SaveChangesAsync(cancellationToken);
            }

            return new AccountChange(user, null);
        }
        finally
        {
            _mutate.Release();
        }
    }

    /// <summary>The policy of 06 §4: at least 10 characters, no other composition rules.</summary>
    public static string? CheckPassword(string? password) =>
        password is null || password.Length < MinPasswordLength ? $"Use at least {MinPasswordLength} characters."
        : password.Length > MaxPasswordLength ? $"Use at most {MaxPasswordLength} characters."
        : null;

    public static string? CheckUserName(string? userName) =>
        string.IsNullOrWhiteSpace(userName) || userName.Length > 64 || userName.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            ? "Use 1 to 64 letters, digits, dots, dashes or underscores."
            : null;

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

/// <summary>The result of an Owner or disabled-state change; <see cref="Code"/> is a stable API code when refused.</summary>
public sealed record AccountChange(UserRecord? User, string? Code);
