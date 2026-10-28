using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
/// DPAPI in the <c>CurrentUser</c> scope of the account Core runs as: as the service only LocalSystem can decrypt
/// (ADR-0009). One file per secret under <c>secrets\</c>; the entropy binds each blob to its scope and name.
/// </summary>
public sealed partial class DpapiSecretStore(IOptions<GameWardenOptions> options, ILogger<DpapiSecretStore> logger) : ISecretStore
{
    private const string Extension = ".dpapi";

    public void Set(string scope, string name, string value)
    {
        var path = PathOf(scope, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy(scope, name), DataProtectionScope.CurrentUser);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, blob);
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
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(scope, name), DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning("Secret {Scope}/{Name} cannot be decrypted ({Reason}); it must be set again.", scope, name, ex.Message);
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

    private static byte[] Entropy(string scope, string name) => Encoding.UTF8.GetBytes($"GameWarden.secret.v1:{scope}/{name}");

    [GeneratedRegex("^(system|instances/[a-z0-9][a-z0-9-]{1,62})$")]
    private static partial Regex Scope();
}
