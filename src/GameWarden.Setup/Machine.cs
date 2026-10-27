using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace GameWarden.Setup;

/// <summary>Machine-wide changes: protected folders (ADR-0009), the machine PATH, the Apps &amp; features entry.</summary>
internal static unsafe partial class Machine
{
    private const string EnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Inheritance off; SYSTEM and Administrators only (data root, C:\ProgramData\GameWarden).</summary>
    public static void CreateProtectedFolder(string path)
    {
        var info = new DirectoryInfo(path);
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
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        info.SetAccessControl(security);
    }

    /// <summary>Adds or removes a folder on the machine PATH, keeping it REG_EXPAND_SZ, and tells running programs.</summary>
    public static void SetOnPath(string folder, bool present)
    {
        using var key = Registry.LocalMachine.OpenSubKey(EnvironmentKey, writable: true)
            ?? throw new InvalidOperationException("The machine environment key is missing.");
        var current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty;
        var updated = SetupPlan.WithPathEntry(current, folder, present);
        if (updated == current)
        {
            return;
        }

        key.SetValue("Path", updated, RegistryValueKind.ExpandString);
        fixed (char* environment = "Environment")
        {
            SendMessageTimeoutW(HwndBroadcast, WmSettingChange, 0, environment, SmtoAbortIfHung, 5000, out _);
        }
    }

    public static void WriteUninstallEntry(InstallLayout layout, string version)
    {
        using var key = Registry.LocalMachine.CreateSubKey(InstallLayout.UninstallKey);
        var setup = Path.Combine(layout.VersionFolder(version), "GameWarden.Setup.exe");
        key.SetValue("DisplayName", "GameWarden");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "GameWarden (open source)");
        key.SetValue("URLInfoAbout", "https://github.com/vinh466/GameWarden");
        key.SetValue("InstallLocation", layout.InstallRoot);
        key.SetValue("DisplayIcon", layout.CoreExe(version));
        key.SetValue("UninstallString", $"\"{setup}\" uninstall");
        key.SetValue("QuietUninstallString", $"\"{setup}\" uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(FolderSize(layout.VersionFolder(version)) / 1024), RegistryValueKind.DWord);
    }

    public static void DeleteUninstallEntry() => Registry.LocalMachine.DeleteSubKeyTree(InstallLayout.UninstallKey, throwOnMissingSubKey: false);

    public static long FolderSize(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0;

    private const nint HwndBroadcast = 0xffff;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SendMessageTimeoutW(nint window, uint message, nint wParam, char* lParam, uint flags, uint timeoutMs, out nint result);
}
