namespace GameWarden.Core.Agents;

/// <summary>
/// The permissions an Agent client may hold (docs/15). Endpoints declare one; a client with
/// <c>TrustedFull</c> bypasses the list, which is the only wildcard in the model.
/// </summary>
public static class AgentPermissions
{
    // Observe.
    public const string StatusRead = "agent.status.read";
    public const string InstancesRead = "agent.instances.read";
    public const string JobsRead = "agent.jobs.read";
    public const string ActivityRead = "agent.activity.read";
    public const string LogsRead = "agent.logs.read";
    public const string MetricsRead = "agent.metrics.read";
    public const string SettingsRead = "agent.settings.read";
    public const string ModsRead = "agent.mods.read";

    // Operate.
    public const string InstancesControl = "agent.instances.control";
    public const string ConsoleWrite = "agent.instances.console.write";
    public const string InstancesInstall = "agent.instances.install";
    public const string InstancesUpdate = "agent.instances.update";

    // Write.
    public const string SettingsWrite = "agent.instances.settings.write";
    public const string ModsWrite = "agent.instances.mods.write";
    public const string ConfigWrite = "agent.instances.config.write";

    // Diagnose.
    public const string DiagnosticsSystem = "agent.diagnostics.system";
    public const string DiagnosticsProcesses = "agent.diagnostics.processes";
    public const string DiagnosticsNetwork = "agent.diagnostics.network";
    public const string DiagnosticsStorage = "agent.diagnostics.storage";
    public const string DiagnosticsEvents = "agent.diagnostics.events";
    public const string DiagnosticsFiles = "agent.diagnostics.files";
    public const string DiagnosticsBundles = "agent.diagnostics.bundles";
    public const string RunbooksRead = "agent.runbooks.read";
    public const string RunbooksRun = "agent.runbooks.run";

    // Admin.
    public const string InstancesDelete = "agent.instances.delete";
    public const string BackupsRestore = "agent.backups.restore";
    public const string SystemUpdate = "agent.system.update";
    public const string SystemSettings = "agent.system.settings";
    public const string AdaptersManage = "agent.adapters.manage";

    public static readonly IReadOnlyList<AgentPermissionInfo> All =
    [
        new(StatusRead, "observe", "Read the Agent status summary.", "low"),
        new(InstancesRead, "observe", "Read instances and their console tail.", "low"),
        new(JobsRead, "observe", "Read jobs and job logs.", "low"),
        new(ActivityRead, "observe", "Read the activity feed.", "low"),
        new(LogsRead, "observe", "Read instance and job logs.", "medium"),
        new(MetricsRead, "observe", "Read CPU and RAM metrics.", "low"),
        new(SettingsRead, "observe", "Read instance settings; secrets stay masked.", "medium"),
        new(ModsRead, "observe", "Read mod sets, logs and configs.", "medium"),

        new(InstancesControl, "operate", "Start, stop and restart game servers.", "medium"),
        new(ConsoleWrite, "operate", "Send allow-listed console commands.", "medium"),
        new(InstancesInstall, "operate", "Install game files with SteamCMD.", "medium"),
        new(InstancesUpdate, "operate", "Update game files with SteamCMD.", "medium"),

        new(SettingsWrite, "write", "Change instance settings.", "high"),
        new(ModsWrite, "write", "Change mod sets and mod configs; mods are code.", "high"),
        new(ConfigWrite, "write", "Write rendered config files.", "high"),

        new(DiagnosticsSystem, "diagnose", "Read OS, service, disk and prerequisite diagnostics.", "high"),
        new(DiagnosticsProcesses, "diagnose", "Read processes and redacted command lines.", "high"),
        new(DiagnosticsNetwork, "diagnose", "Read interfaces, listeners, connections and firewall rules.", "high"),
        new(DiagnosticsStorage, "diagnose", "Read volumes, folder sizes and save inventory.", "high"),
        new(DiagnosticsEvents, "diagnose", "Read Windows Event Log entries.", "high"),
        new(DiagnosticsFiles, "diagnose", "Read allow-listed instance files, redacted.", "high"),
        new(DiagnosticsBundles, "diagnose", "Create and download redacted diagnostic bundles.", "high"),
        new(RunbooksRead, "diagnose", "List diagnostic runbooks.", "low"),
        new(RunbooksRun, "diagnose", "Run diagnostic runbooks.", "high"),

        new(InstancesDelete, "admin", "Delete an instance and its files.", "critical"),
        new(BackupsRestore, "admin", "Restore a backup.", "critical"),
        new(SystemUpdate, "admin", "Update GameWarden itself.", "critical"),
        new(SystemSettings, "admin", "Change panel, VPN and Agent settings.", "critical"),
        new(AdaptersManage, "admin", "Reload or change adapters; adapters are code.", "critical"),
    ];

    public static readonly IReadOnlySet<string> Names = All.Select(permission => permission.Key).ToHashSet(StringComparer.Ordinal);

    public static readonly IReadOnlyList<AgentPermissionPreset> Presets =
    [
        new("observer", "Observer", "Read status, instances, jobs, activity and logs.", All.Where(p => p.Group == "observe").Select(p => p.Key).ToArray()),
        new("operator", "Operator", "Observe and run the servers: start, stop, restart, install, update, console.", All.Where(p => p.Group is "observe" or "operate").Select(p => p.Key).ToArray()),
        new("debugger", "Debugger", "Observe plus deep diagnostics and runbooks.", All.Where(p => p.Group is "observe" or "diagnose").Select(p => p.Key).ToArray()),
        new("full", "Full", "Every permission that exists now.", All.Select(p => p.Key).ToArray()),
    ];

    public static bool IsKnown(string permission) => Names.Contains(permission);
}

public sealed record AgentPermissionInfo(string Key, string Group, string Description, string Risk);

public sealed record AgentPermissionPreset(string Key, string Name, string Description, IReadOnlyList<string> Permissions);
