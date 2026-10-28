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
            await context.SaveChangesAsync(cancellationToken);
        }

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
                PasswordHash = string.Empty,
                SecurityStamp = NewStamp(),
                IsOwner = true,
                CreatedAt = now,
                PasswordChangedAt = now,
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
