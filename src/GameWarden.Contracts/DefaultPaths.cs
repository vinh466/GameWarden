namespace GameWarden.Contracts;

/// <summary>
/// Where Core and the CLI look when nothing is configured, so both agree (08 §1, 02 §11). Windows is the production
/// host; every other OS is the development platform of ADR-0016 and uses the development paths.
/// </summary>
public static class DefaultPaths
{
    /// <summary>The production data root on Windows (08 §1).</summary>
    public const string WindowsDataRoot = @"C:\GameWarden";

    /// <summary><c>%LOCALAPPDATA%\GameWardenDev</c> on Windows, <c>~/.local/share/GameWardenDev</c> on Linux (02 §11).</summary>
    public static string DevelopmentDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameWardenDev");

    /// <summary>The CLI token of a development Core: <c>cli.token</c> in <see cref="DevelopmentDataRoot"/>.</summary>
    public static string DevelopmentCliToken => Path.Combine(DevelopmentDataRoot, "cli.token");

    /// <summary><see cref="WindowsDataRoot"/> on Windows; elsewhere <see cref="DevelopmentDataRoot"/> (ADR-0016).</summary>
    public static string DataRoot => OperatingSystem.IsWindows() ? WindowsDataRoot : DevelopmentDataRoot;

    /// <summary><c>C:\ProgramData\GameWarden\cli.token</c> on Windows (02 §8); elsewhere <see cref="DevelopmentCliToken"/>.</summary>
    public static string CliToken => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameWarden", "cli.token")
        : DevelopmentCliToken;
}
