using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GameWarden.Core.Hosting;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Secrets;

/// <summary>
/// Secrets (server passwords, the run-as password, later the Discord webhook). Values never go into the database,
/// API responses or logs; the API only says whether a secret is set (05 §2).
/// </summary>
public interface ISecretStore
{
    void Set(string scope, string name, string value);

    /// <summary>The value, or null when it is not set or cannot be decrypted (e.g. after moving to another machine, 08 §8).</summary>
    string? Get(string scope, string name);

    IReadOnlyList<string> Names(string scope);

    bool Delete(string scope, string name);

    void DeleteScope(string scope);
}

public static partial class SecretScopes
{
    public const string System = "system";

    public static string Instance(string instanceId) => $"instances/{instanceId}";

    public static bool IsValidName(string name) => Name().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex Name();
}

/// <summary>
/// One file per secret under <c>secrets\&lt;scope&gt;\&lt;name&gt;&lt;extension&gt;</c>: the layout and name rules every store
/// shares. A store only says how a value is kept in its file; its own extension keeps the stores from reading each
/// other's files.
/// </summary>
public abstract partial class FileSecretStore(IOptions<GameWardenOptions> options, ILogger logger) : ISecretStore
{
    /// <summary>The file extension of this store's secrets, with the dot.</summary>
    protected abstract string Extension { get; }

    public void Set(string scope, string name, string value)
    {
        var path = PathOf(scope, name);
        CreateFolder(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        WriteFile(temporary, Protect(Encoding.UTF8.GetBytes(value), scope, name));
        File.Move(temporary, path, overwrite: true);
    }

    public string? Get(string scope, string name)
    {
        var path = PathOf(scope, name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(path), scope, name));
        }
        catch (CryptographicException ex)
        {
            // Names only; never values. The name itself is left out too, as names such as "password" read like secrets in logs.
            logger.LogWarning("A secret of {Scope} cannot be decrypted ({Reason}); it must be set again.", LogText.Clean(scope), ex.Message);
            return null;
        }
    }

    public IReadOnlyList<string> Names(string scope)
    {
        var folder = FolderOf(scope);
        return Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*" + Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
    }

    public bool Delete(string scope, string name)
    {
        var path = PathOf(scope, name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public void DeleteScope(string scope)
    {
        var folder = FolderOf(scope);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The file content for <paramref name="value"/>.</summary>
    protected abstract byte[] Protect(byte[] value, string scope, string name);

    /// <summary>The value back from the file content; throws <see cref="CryptographicException"/> when it cannot.</summary>
    protected abstract byte[] Unprotect(byte[] content, string scope, string name);

    protected virtual void CreateFolder(string folder) => Directory.CreateDirectory(folder);

    protected virtual void WriteFile(string path, byte[] content) => File.WriteAllBytes(path, content);

    private string PathOf(string scope, string name)
    {
        if (!SecretScopes.IsValidName(name))
        {
            throw new ArgumentException($"Invalid secret name '{name}'.", nameof(name));
        }

        return Path.Combine(FolderOf(scope), name + Extension);
    }

    private string FolderOf(string scope)
    {
        if (!Scope().IsMatch(scope))
        {
            throw new ArgumentException($"Invalid secret scope '{scope}'.", nameof(scope));
        }

        return Path.Combine([options.Value.SecretsPath, .. scope.Split('/')]);
    }

    [GeneratedRegex("^(system|instances/[a-z0-9][a-z0-9-]{1,62})$")]
    private static partial Regex Scope();
}

/// <summary>
/// DPAPI in the <c>CurrentUser</c> scope of the account Core runs as: as the service only LocalSystem can decrypt
/// (ADR-0009). One file per secret under <c>secrets\</c>; the entropy binds each blob to its scope and name.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(IOptions<GameWardenOptions> options, ILogger<DpapiSecretStore> logger) : FileSecretStore(options, logger)
{
    protected override string Extension => ".dpapi";

    protected override byte[] Protect(byte[] value, string scope, string name) =>
        ProtectedData.Protect(value, Entropy(scope, name), DataProtectionScope.CurrentUser);

    protected override byte[] Unprotect(byte[] content, string scope, string name) =>
        ProtectedData.Unprotect(content, Entropy(scope, name), DataProtectionScope.CurrentUser);

    private static byte[] Entropy(string scope, string name) => Encoding.UTF8.GetBytes($"GameWarden.secret.v1:{scope}/{name}");
}

/// <summary>
/// The development platform's secrets (ADR-0016): plain UTF-8 in files only the account Core runs as can read (mode
/// 0600, folders 0700). Not encrypted, so never chosen on Windows (<see cref="Platform.PlatformServices"/>).
/// </summary>
public sealed class DevelopmentSecretStore(IOptions<GameWardenOptions> options, ILogger<DevelopmentSecretStore> logger) : FileSecretStore(options, logger)
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyFolder = OwnerOnlyFile | UnixFileMode.UserExecute;

    protected override string Extension => ".secret";

    protected override byte[] Protect(byte[] value, string scope, string name) => value;

    protected override byte[] Unprotect(byte[] content, string scope, string name) => content;

    protected override void CreateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, OwnerOnlyFolder);
        }
    }

    protected override void WriteFile(string path, byte[] content)
    {
        File.Delete(path); // a mode applies only to a file this call creates
        var file = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            file.UnixCreateMode = OwnerOnlyFile;
        }

        using var stream = new FileStream(path, file);
        stream.Write(content);
    }
}
