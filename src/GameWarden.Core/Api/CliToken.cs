using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using GameWarden.Core.Hosting;
using GameWarden.Core.Isolation;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Api;

/// <summary>
/// The CLI's bearer token (docs/02-architecture.md §8): created on first start, readable only by
/// Administrators, SYSTEM and the account Core runs as; accepted only on loopback.
/// </summary>
public sealed class CliToken
{
    public CliToken(IOptions<GameWardenOptions> options, IsolationMode isolation, ILogger<CliToken> logger)
    {
        var path = options.Value.ResolvedCliTokenPath;
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
            Value = File.ReadAllText(path).Trim();
            return;
        }

        Value = Base64Url(RandomNumberGenerator.GetBytes(32));

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

        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
        stream.Write(Encoding.UTF8.GetBytes(Value));
    }

    public string Value { get; }

    public bool Accepts(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(authorization[prefix.Length..]), Encoding.UTF8.GetBytes(Value));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool OwnedByAdministrators(string path)
    {
        var owner = new FileInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        return owner is SecurityIdentifier sid
            && (sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
    }
}

/// <summary>The API is CLI-only until the panel login exists (M2): loopback plus the CLI token.</summary>
internal sealed class CliTokenFilter(CliToken token) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
        {
            return ApiProblems.Create(StatusCodes.Status403Forbidden, "forbidden", "The CLI API is available on loopback only.");
        }

        if (!token.Accepts(http.Request.Headers.Authorization))
        {
            return ApiProblems.Create(StatusCodes.Status401Unauthorized, "unauthenticated", "Missing or wrong CLI token.");
        }

        return await next(context);
    }
}
