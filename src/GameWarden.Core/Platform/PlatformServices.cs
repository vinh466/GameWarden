using System.Runtime.Versioning;
using GameWarden.Core.Checks;
using GameWarden.Core.Hosting;
using GameWarden.Core.Isolation;
using GameWarden.Core.Network;
using GameWarden.Core.Processes;
using GameWarden.Core.Secrets;

namespace GameWarden.Core.Platform;

/// <summary>
/// The one place that picks Core's platform adapters (ADR-0016), from the OS and the isolation mode: Windows gets the
/// production adapters, Linux the development platform, and any other OS is refused.
/// </summary>
public static class PlatformServices
{
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        var isolation = IsolationMode.From(configuration);
        services.AddSingleton(isolation);
        if (OperatingSystem.IsWindows())
        {
            AddWindows(services, isolation);
        }
        else if (OperatingSystem.IsLinux())
        {
            AddDevelopmentPlatform(services);
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"GameWarden Core runs on Windows, and on Linux as a development platform; not on {Environment.OSVersion} (ADR-0016).");
        }

        return services;
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindows(IServiceCollection services, IsolationMode isolation)
    {
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<IMachine, WindowsMachine>();
        services.AddSingleton<IPrerequisiteChecker, WindowsPrerequisiteChecker>();
        if (isolation.Enabled)
        {
            services.AddSingleton<RunAsAccount>();
            services.AddSingleton<IProcessHost, RunAsUserProcessHost>();
            services.AddSingleton<IInstanceFiles, ImpersonatedInstanceFiles>();
            services.AddSingleton<IDataRootAcl, WindowsDataRootAcl>();
            services.AddSingleton<IFirewall, WindowsFirewall>();
        }
        else
        {
            AddUnisolated(services);
        }
    }

    /// <summary>Not a deployment target: no isolation, firewall, service host, self-update or SteamCMD download (ADR-0016).</summary>
    private static void AddDevelopmentPlatform(IServiceCollection services)
    {
        services.AddSingleton<ISecretStore, DevelopmentSecretStore>();
        services.AddSingleton<IMachine, LinuxMachine>();
        services.AddSingleton<IPrerequisiteChecker, DevelopmentPrerequisiteChecker>();
        AddUnisolated(services);
    }

    /// <summary>Core and the games run as the same user: development, CI, and Windows with isolation off (02 §11).</summary>
    private static void AddUnisolated(IServiceCollection services)
    {
        services.AddSingleton<IProcessHost, CurrentUserProcessHost>();
        services.AddSingleton<IInstanceFiles, DirectInstanceFiles>();
        services.AddSingleton<IDataRootAcl, PlainDataRoot>();
        services.AddSingleton<IFirewall, NoFirewall>();
    }
}
