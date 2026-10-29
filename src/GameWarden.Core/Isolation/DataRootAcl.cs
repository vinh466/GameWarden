using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Isolation;

/// <summary>Folders of the data root and their access rules (ADR-0009 §1, 08 §1).</summary>
public interface IDataRootAcl
{
    /// <summary>Core start-up: creates the data root's folders and applies the rules where missing.</summary>
    void EnsureLayout();

    /// <summary>Creates an instance's folders as Core and gives <c>gamewarden-run</c> its access where missing.</summary>
    void PrepareInstance(InstancePaths paths);
}

/// <summary>Development and CI: plain folders, the current user's default access.</summary>
public sealed class PlainDataRoot(IOptions<GameWardenOptions> options) : IDataRootAcl
{
    public void EnsureLayout()
    {
        foreach (var folder in DataRootFolders.All(options.Value))
        {
            Directory.CreateDirectory(folder);
        }
    }

    public void PrepareInstance(InstancePaths paths) => paths.Create();
}

/// <summary>
/// Production: the data root has inheritance off and only SYSTEM and Administrators; <c>gamewarden-run</c> gets
/// read on <c>adapters\</c>, modify on <c>tools\steamcmd\</c> and on each instance's five folders. Rules are only
/// added when missing, and never on a link: re-applying them on a folder a mod turned into a junction would hand
/// the target to the run-as user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDataRootAcl(IOptions<GameWardenOptions> options, RunAsAccount account, ILogger<WindowsDataRootAcl> logger) : IDataRootAcl
{
    // Enough to open paths below a folder and list it, without reading files in it.
    private const FileSystemRights Traverse = FileSystemRights.Traverse | FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;
    private const InheritanceFlags Inherited = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    public void EnsureLayout()
    {
        var o = options.Value;
        Protect(o.DataRoot, logger);
        foreach (var folder in DataRootFolders.All(o))
        {
            Directory.CreateDirectory(folder);
        }

        Grant(o.DataRoot, Traverse, InheritanceFlags.None);
        Grant(o.InstancesPath, Traverse, InheritanceFlags.None);
        Grant(o.ToolsPath, Traverse, InheritanceFlags.None);
        Grant(o.OwnerAdaptersPath, FileSystemRights.ReadAndExecute, Inherited);
        Grant(o.SteamCmdFolder, FileSystemRights.Modify, Inherited);
    }

    public void PrepareInstance(InstancePaths paths)
    {
        paths.Create();
        Grant(paths.Root, FileSystemRights.ReadAndExecute, InheritanceFlags.None);
        foreach (var folder in paths.RunAsFolders)
        {
            Grant(folder, FileSystemRights.Modify, Inherited);
        }
    }

    /// <summary>Inheritance off, SYSTEM and Administrators only. Setup does this on install; Core repeats it if someone undid it.</summary>
    public static void Protect(string root, ILogger logger)
    {
        var info = new DirectoryInfo(root);
        info.Create();
        if (info.GetAccessControl().AreAccessRulesProtected)
        {
            return;
        }

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null), FileSystemRights.FullControl, Inherited, PropagationFlags.None, AccessControlType.Allow));
        }

        info.SetAccessControl(security);
        logger.LogInformation("Protected the data root {Root}: SYSTEM and Administrators only (ADR-0009).", root);
    }

    private void Grant(string path, FileSystemRights rights, InheritanceFlags inheritance)
    {
        var info = new DirectoryInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            logger.LogError("{Path} is a link (junction or symlink); its access rules are left alone (ADR-0009). Remove the link.", LogText.Clean(path));
            return;
        }

        var security = info.GetAccessControl();
        var present = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(rule => rule.IdentityReference.Equals(account.Sid)
                && rule.AccessControlType == AccessControlType.Allow
                && rule.InheritanceFlags == inheritance
                && (rule.FileSystemRights & rights) == rights);
        if (present)
        {
            return;
        }

        security.AddAccessRule(new FileSystemAccessRule(account.Sid, rights, inheritance, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
        logger.LogInformation("Granted {User} {Rights} on {Path}.", account.UserName, rights, LogText.Clean(path));
    }
}

internal static class DataRootFolders
{
    public static IEnumerable<string> All(GameWardenOptions options) =>
    [
        options.DataRoot,
        options.InstancesPath,
        options.OwnerAdaptersPath,
        options.ToolsPath,
        options.SteamCmdFolder,
        options.SecretsPath,
        options.CoreLogsPath,
        options.JobLogsPath,
        options.UpdatesPath,
    ];
}
