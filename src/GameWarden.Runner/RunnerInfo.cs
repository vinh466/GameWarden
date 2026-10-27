using System.Reflection;

namespace GameWarden.Runner;

internal static class RunnerInfo
{
    public static string Version { get; } =
        typeof(RunnerInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}
