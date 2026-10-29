using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameWarden.Core.Checks;

/// <summary>The prerequisites of 08 §2 from the registry and the system folders.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPrerequisiteChecker : IPrerequisiteChecker
{
    public PrerequisiteStatus Check(string id) => id switch
    {
        Prerequisites.VcRedistX64 => new(id, VcRedistInstalled("x64", Environment.SystemDirectory),
            "Microsoft Visual C++ Redistributable 2015-2022 (x64): https://aka.ms/vs/17/release/vc_redist.x64.exe"),
        Prerequisites.VcRedistX86 => new(id, VcRedistInstalled("x86", Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)),
            "Microsoft Visual C++ Redistributable 2015-2022 (x86): https://aka.ms/vs/17/release/vc_redist.x86.exe"),
        Prerequisites.DirectXLegacy => new(id,
            File.Exists(Path.Combine(Environment.SystemDirectory, "d3dx9_43.dll")) && File.Exists(Path.Combine(Environment.SystemDirectory, "xinput1_3.dll")),
            "DirectX End-User Runtime (June 2010) from Microsoft"),
        _ => Prerequisites.Unknown(id),
    };

    /// <summary>The VC++ 14.x runtime registers itself under <c>VC\Runtimes</c>; its DLLs in the system folder count too.</summary>
    private static bool VcRedistInstalled(string architecture, string systemFolder)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{architecture}");
            if (key?.GetValue("Installed") is int installed && installed == 1)
            {
                return true;
            }
        }

        return new[] { "vcruntime140.dll", "msvcp140.dll" }.All(dll => File.Exists(Path.Combine(systemFolder, dll)));
    }
}
