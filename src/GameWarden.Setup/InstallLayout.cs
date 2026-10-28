using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace GameWarden.Setup;

/// <summary>
/// Where GameWarden lives (08 §1, 10 §5). Binaries: <c>C:\Program Files\GameWarden\versions\&lt;v&gt;\</c>, one flat folder
/// per version with Core, Runner, CLI and Setup sharing one runtime. The installed state is kept in
/// <c>HKLM\SOFTWARE\GameWarden</c>.
/// </summary>
internal sealed record InstallLayout(string InstallRoot, string DataRoot)
{
    public const string ServiceName = "GameWardenCore";
    public const string DefaultDataRoot = @"C:\GameWarden";
    public const string StateKey = @"SOFTWARE\GameWarden";
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GameWarden";
    public const int DefaultPanelPort = 8765;

    public static string DefaultInstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameWarden");

    public static string ProgramDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameWarden");

    public static string TokenPath => Path.Combine(ProgramDataFolder, "cli.token");

    public string VersionsPath => Path.Combine(InstallRoot, "versions");

    public string ShimPath => Path.Combine(InstallRoot, "gamewarden.cmd");

    public string UpdatesPath => Path.Combine(DataRoot, "updates");

    public string DatabasePath => Path.Combine(DataRoot, "db", "gamewarden.db");

    public string LastResultPath => Path.Combine(UpdatesPath, "last-result.json");

    public string SetupLogsPath => Path.Combine(DataRoot, "logs", "setup");

    public string VersionFolder(string version) => Path.Combine(VersionsPath, version);

    public string CoreExe(string version) => Path.Combine(VersionFolder(version), "GameWarden.Core.exe");

    public string ServiceCommandLine(string version) => $"\"{CoreExe(version)}\" --service";

    public IReadOnlyList<string> InstalledVersions() =>
        Directory.Exists(VersionsPath)
            ? [.. Directory.GetDirectories(VersionsPath).Select(Path.GetFileName).OfType<string>().Where(name => File.Exists(CoreExe(name)))]
            : [];

    /// <summary>The panel/API port: Owner overrides in gamewarden.json win over the default (08 §6).</summary>
    public int PanelPort()
    {
        try
        {
            var file = Path.Combine(DataRoot, "gamewarden.json");
            if (File.Exists(file)
                && JsonDocument.Parse(File.ReadAllText(file)).RootElement.TryGetProperty("GameWarden", out var section)
                && section.TryGetProperty("PanelPort", out var port))
            {
                return port.ValueKind == JsonValueKind.Number ? port.GetInt32() : int.Parse(port.GetString()!, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or UnauthorizedAccessException)
        {
        }

        return DefaultPanelPort;
    }
}

/// <summary>What Setup recorded about the installation.</summary>
internal sealed record InstallState(string InstallRoot, string DataRoot, string? CurrentVersion, string? PreviousVersion)
{
    public InstallLayout Layout => new(InstallRoot, DataRoot);

    public static InstallState? Load()
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallLayout.StateKey);
        if (key?.GetValue("InstallRoot") is not string installRoot)
        {
            return null;
        }

        return new InstallState(
            installRoot,
            key.GetValue("DataRoot") as string ?? InstallLayout.DefaultDataRoot,
            key.GetValue("CurrentVersion") as string,
            key.GetValue("PreviousVersion") as string);
    }

    public void Save()
    {
        using var key = Registry.LocalMachine.CreateSubKey(InstallLayout.StateKey);
        key.SetValue("InstallRoot", InstallRoot);
        key.SetValue("DataRoot", DataRoot);
        SetOrDelete(key, "CurrentVersion", CurrentVersion);
        SetOrDelete(key, "PreviousVersion", PreviousVersion);
    }

    public static void Delete() => Registry.LocalMachine.DeleteSubKeyTree(InstallLayout.StateKey, throwOnMissingSubKey: false);

    private static void SetOrDelete(RegistryKey key, string name, string? value)
    {
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value);
        }
    }
}
