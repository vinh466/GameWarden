using System.Runtime.Versioning;
using System.Security.Principal;

namespace GameWarden.Core.Isolation;

/// <summary>
/// The only way Core touches files that <c>gamewarden-run</c> can write (ADR-0009 §3): as the service it impersonates
/// that user, so a link planted by a mod cannot make SYSTEM read or write elsewhere. Keep the work synchronous;
/// impersonation does not flow across awaits.
/// </summary>
public interface IInstanceFiles
{
    T Run<T>(Func<T> action);
}

public static class InstanceFilesExtensions
{
    public static void Run(this IInstanceFiles files, Action action) => files.Run(() =>
    {
        action();
        return true;
    });
}

/// <summary>Development and CI: Core and the games run as the same user (02 §11).</summary>
public sealed class DirectInstanceFiles : IInstanceFiles
{
    public T Run<T>(Func<T> action) => action();
}

[SupportedOSPlatform("windows")]
public sealed class ImpersonatedInstanceFiles(RunAsAccount account) : IInstanceFiles
{
    public T Run<T>(Func<T> action) => WindowsIdentity.RunImpersonated(account.Token, action);
}
