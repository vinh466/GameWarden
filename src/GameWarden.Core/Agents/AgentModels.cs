using GameWarden.Core.Supervision;

namespace GameWarden.Core.Agents;

/// <summary>An Agent client as the panel shows it; never includes a token.</summary>
public sealed record AgentClientView(
    string Id,
    string Name,
    string? Description,
    bool Enabled,
    bool TrustedFull,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> AllowedNetworks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSeenAt,
    string? LastSeenIp,
    string? LastSeenVersion)
{
    public static AgentClientView From(Data.AgentClientRecord record) => new(
        record.Id,
        record.Name,
        record.Description,
        record.Enabled,
        record.TrustedFull,
        AgentJson.Strings(record.PermissionsJson),
        AgentJson.Strings(record.AllowedNetworksJson),
        record.CreatedAt,
        record.UpdatedAt,
        record.LastSeenAt,
        record.LastSeenIp,
        record.LastSeenVersion);
}

public sealed record CreateAgentClientRequest(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Permissions,
    bool? TrustedFull,
    IReadOnlyList<string>? AllowedNetworks);

public sealed record UpdateAgentClientRequest(
    string? Name,
    string? Description,
    bool? Enabled,
    IReadOnlyList<string>? Permissions,
    bool? TrustedFull,
    IReadOnlyList<string>? AllowedNetworks);

public sealed record AgentTokenView(
    string Id,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    string? LastUsedIp)
{
    public static AgentTokenView From(Data.AgentTokenRecord record) => new(
        record.Id,
        record.Name,
        record.Prefix,
        record.CreatedAt,
        record.ExpiresAt,
        record.RevokedAt,
        record.LastUsedAt,
        record.LastUsedIp);
}

public sealed record CreateAgentTokenRequest(string? Name, int? ExpiresInDays);

/// <summary>The only response that carries the plaintext token; it is shown once and never stored.</summary>
public sealed record AgentTokenCreated(string Token, AgentTokenView TokenInfo);

public sealed record AgentPermissionCatalog(
    IReadOnlyList<AgentPermissionInfo> Permissions,
    IReadOnlyList<AgentPermissionPreset> Presets);

public sealed record AgentModeWindow(bool Enabled, DateTimeOffset? ExpiresAt, string? Reason);

public sealed record AgentSettingsView(
    bool Enabled,
    bool RequireAdminMode,
    AgentModeWindow Diagnostics,
    AgentModeWindow Admin,
    IReadOnlyList<string> AllowedNetworks,
    DateTimeOffset UpdatedAt)
{
    public static AgentSettingsView From(Data.AgentSettingsRecord record, DateTimeOffset now)
    {
        static AgentModeWindow Window(bool enabled, DateTimeOffset? expiresAt, string? reason, DateTimeOffset now) =>
            new(enabled && expiresAt > now, expiresAt, reason);

        return new(
            record.Enabled,
            record.RequireAdminMode,
            Window(record.DiagnosticsEnabled, record.DiagnosticsExpiresAt, record.DiagnosticsReason, now),
            Window(record.AdminEnabled, record.AdminExpiresAt, record.AdminReason, now),
            AgentJson.Strings(record.AllowedNetworksJson),
            record.UpdatedAt);
    }
}

public sealed record UpdateAgentSettingsRequest(
    bool? Enabled,
    bool? RequireAdminMode,
    AgentModeRequest? Diagnostics,
    AgentModeRequest? Admin,
    IReadOnlyList<string>? AllowedNetworks);

public sealed record AgentModeRequest(bool Enabled, int? TtlMinutes, string? Reason);

public sealed record AgentMeView(
    string ClientId,
    string ClientName,
    bool TrustedFull,
    IReadOnlyList<string> Permissions,
    string TokenId,
    DateTimeOffset TokenExpiresAt,
    AgentModeWindow Diagnostics,
    AgentModeWindow Admin,
    string ApiVersion,
    DateTimeOffset ServerTime);

public sealed record AgentCoreView(string Version, long UptimeSec, string Health);

public sealed record AgentMachineView(int CpuCores, long RamTotalMb, long RamFreeMb, long DiskFreeMb);

public sealed record AgentStatusView(
    AgentCoreView Core,
    AgentMeView Agent,
    AgentMachineView Machine,
    IReadOnlyList<AgentInstanceView> Instances,
    IReadOnlyList<AgentProblemView> Problems);

public sealed record AgentProblemView(string Kind, string Message, string? InstanceId, DateTimeOffset? At);

public sealed record AgentInstanceView(
    string Id,
    string DisplayName,
    string AdapterId,
    string State,
    string DesiredState,
    string? ActiveJobId,
    int? GamePid,
    int RecentRestarts,
    string? LastProblem,
    IReadOnlyDictionary<string, int> Ports)
{
    public static AgentInstanceView From(InstanceSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.DisplayName,
        snapshot.AdapterId,
        AgentJson.Camel(snapshot.State),
        AgentJson.Camel(snapshot.DesiredState),
        snapshot.ActiveJobId,
        snapshot.GamePid,
        snapshot.RecentRestarts,
        snapshot.LastProblem,
        snapshot.Ports);
}

public sealed record AgentCommandResponse(string Message, AgentInstanceView Instance);

public sealed record AgentLogLine(DateTimeOffset? At, string Level, string Text);

public sealed record AgentLogsView(
    string Source,
    string File,
    IReadOnlyList<AgentLogLine> Lines,
    string? NextCursor,
    bool Truncated,
    bool Redacted);

public sealed record AgentSystemDiagnostics(
    string Os,
    string Architecture,
    string Runtime,
    string MachineName,
    long UptimeSec,
    int ProcessId,
    string DataRoot,
    bool Isolation,
    IReadOnlyList<string> VpnRanges,
    IReadOnlyList<string> PanelNetworks,
    AgentMachineView Machine,
    DateTimeOffset CollectedAt);

public sealed record AgentProcessInfo(
    int Pid,
    string Name,
    long WorkingSetMb,
    DateTimeOffset? StartedAt,
    string? CommandLine);

public sealed record AgentStorageVolume(string Name, string Type, long TotalMb, long FreeMb);

public sealed record AgentFolderSize(string Path, long SizeMb, DateTimeOffset? Newest, bool TimedOut);

public sealed record AgentStorageView(
    IReadOnlyList<AgentStorageVolume> Volumes,
    IReadOnlyList<AgentFolderSize> DataRoot,
    IReadOnlyList<AgentFolderSize> Instances,
    DateTimeOffset CollectedAt);

public sealed record AgentNetworkInterface(string Name, string Description, string Status, IReadOnlyList<string> Addresses);

public sealed record AgentListener(string Protocol, string Address, int Port);

public sealed record AgentConnection(string Local, string Remote, string State);

public sealed record AgentNetworkView(
    IReadOnlyList<AgentNetworkInterface> Interfaces,
    IReadOnlyList<AgentListener> Listeners,
    IReadOnlyList<AgentConnection> Connections,
    DateTimeOffset CollectedAt);

public sealed record AgentEventEntry(DateTimeOffset At, string Level, string Source, int Id, string Message);

public sealed record AgentEventsView(bool Supported, string? Reason, IReadOnlyList<AgentEventEntry> Entries, DateTimeOffset CollectedAt);

public sealed record CreateAgentBundleRequest(IReadOnlyList<string>? InstanceIds, int? SinceHours);

public sealed record AgentBundleView(
    string Id,
    string Status,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Contents);

public sealed record AgentAuditView(
    long Id,
    DateTimeOffset At,
    string? ClientId,
    string? ClientName,
    string? TokenId,
    string Action,
    string? Target,
    string Outcome,
    int StatusCode,
    string? SourceIp,
    string RequestId,
    long DurationMs,
    string? Details)
{
    public static AgentAuditView From(Data.AgentAuditRecord record) => new(
        record.Id,
        record.At,
        record.ClientId,
        record.ClientName,
        record.TokenId,
        record.Action,
        record.Target,
        record.Outcome,
        record.StatusCode,
        record.SourceIp,
        record.RequestId,
        record.DurationMs,
        record.Details);
}

internal static class AgentJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> Strings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    public static string Write(IEnumerable<string> values) => System.Text.Json.JsonSerializer.Serialize(values, Options);

    public static string Camel<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
