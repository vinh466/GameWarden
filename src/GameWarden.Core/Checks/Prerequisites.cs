using Microsoft.Win32;

namespace GameWarden.Core.Checks;

public sealed record PrerequisiteStatus(string Id, bool Installed, string Hint);

/// <summary>Prerequisites an adapter can declare (08 §2). Core checks them before a start; installing them is up to the Owner for now.</summary>
public static class Prerequisites
{
    public const string VcRedistX64 = "vcredist-x64";
    public const string VcRedistX86 = "vcredist-x86";
    public const string DirectXLegacy = "directx-legacy";

    public static readonly IReadOnlyList<string> Known = [VcRedistX64, VcRedistX86, DirectXLegacy];

    public static PrerequisiteStatus Check(string id) => id switch
    {
        VcRedistX64 => new(id, VcRedistInstalled("x64", Environment.SystemDirectory),
            "Microsoft Visual C++ Redistributable 2015-2022 (x64): https://aka.ms/vs/17/release/vc_redist.x64.exe"),
        VcRedistX86 => new(id, VcRedistInstalled("x86", Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)),
            "Microsoft Visual C++ Redistributable 2015-2022 (x86): https://aka.ms/vs/17/release/vc_redist.x86.exe"),
        DirectXLegacy => new(id,
            File.Exists(Path.Combine(Environment.SystemDirectory, "d3dx9_43.dll")) && File.Exists(Path.Combine(Environment.SystemDirectory, "xinput1_3.dll")),
            "DirectX End-User Runtime (June 2010) from Microsoft"),
        _ => new(id, false, $"Unknown prerequisite '{id}'."),
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
