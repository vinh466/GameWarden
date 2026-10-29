using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using GameWarden.Core.Hosting;
using GameWarden.Core.Isolation;

namespace GameWarden.Core.Access;

/// <summary>
/// A random token in a file readable only by Administrators, SYSTEM and the account Core runs as: the CLI token
/// (docs/02-architecture.md §8) and the one-time setup token (docs/06-security-and-access.md §4). On the development
/// platform the file is readable by its owner only (mode 0600, ADR-0016).
/// </summary>
public static class TokenFile
{
    public static string ReadOrCreate(string path, IsolationMode isolation, ILogger logger)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        if (isolation.Enabled)
        {
            // Users may create files in C:\ProgramData: a token planted before the install would be known to them.
            WindowsDataRootAcl.Protect(folder, logger);
            if (File.Exists(path) && !OwnedByAdministrators(path))
            {
                logger.LogWarning("Replacing {Path}: it was not created by an administrator.", path);
                File.Delete(path);
            }
        }

        if (File.Exists(path))
        {
            return File.ReadAllText(path).Trim();
        }

        var value = NewToken();
        using var stream = OperatingSystem.IsWindows() ? CreateWithAcl(path) : CreateOwnerOnly(path);
        stream.Write(Encoding.UTF8.GetBytes(value));
        return value;
    }

    /// <summary>Compares in constant time, so the answer time does not reveal how much of a guess was right.</summary>
    public static bool Matches(string? candidate, string expected) =>
        candidate is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(expected));

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [SupportedOSPlatform("windows")]
    private static FileStream CreateWithAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[]
                 {
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                     WindowsIdentity.GetCurrent().User!,
                 })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
    }

    /// <summary>Development platform (ADR-0016): readable and writable by the account Core runs as only.</summary>
    [UnsupportedOSPlatform("windows")]
    private static FileStream CreateOwnerOnly(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.CreateNew,
        Access = FileAccess.Write,
        Share = FileShare.None,
        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
    });

    [SupportedOSPlatform("windows")]
    private static bool OwnedByAdministrators(string path)
    {
        var owner = new FileInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        return owner is SecurityIdentifier sid
            && (sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
    }
}
