using GameWarden.Contracts;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Adapters;
using GameWarden.Core.Checks;
using GameWarden.Core.Data;
using GameWarden.Core.Supervision;
using GameWarden.Core.Updates;

namespace GameWarden.Core.Api;

// Response bodies of the API (docs/05-api.md). Named records, not anonymous objects, so the OpenAPI document — and the
// panel's client generated from it (docs/11 §9) — has a schema for every response. Property names are the JSON names.

public sealed record HealthStatus(string Status);

public sealed record LoginRequest(string UserName, string Password);

/// <summary>The signed-in user and the CSRF token the panel sends in <c>X-GW-CSRF</c> (05 §1).</summary>
public sealed record SessionInfo(string UserName, bool IsOwner, string CsrfToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SetupStatus(bool NeedsSetup);

public sealed record CreateOwnerRequest(string SetupToken, string UserName, string Password);

public sealed record SetupUrl(string Url);

public sealed record ResetPasswordRequest(string Password);

public sealed record OwnerPasswordReset(string UserName);

public sealed record SystemInfo(
    string Version,
    DateTimeOffset StartedAt,
    string? InstallRoot,
    UpdateResult? LastUpdate,
    UpdateCheck? UpdateCheck,
    string DataRoot,
    bool Isolation,
    string? RunAsUser,
    IReadOnlyList<string> VpnRanges,
    MachineSnapshot Machine,
    bool SteamCmdInstalled,
    IReadOnlyList<HostCheck> Network,
    IReadOnlyList<PrerequisiteInfo> Prerequisites,
    int AdapterProblems);

/// <param name="NeededBy">Adapters that declare it; a missing prerequisite no adapter needs is only informative.</param>
public sealed record PrerequisiteInfo(string Id, bool Installed, string Hint, IReadOnlyList<string> NeededBy);

public sealed record UninstallCleanupResult(bool AllInstancesStopped, bool FirewallRulesRemoved, string? RunAsUserProblem);

/// <summary>What the panel needs to offer an adapter in the new server wizard (docs/11 §4).</summary>
public sealed record AdapterSummary(
    string Id,
    string Name,
    string Version,
    string Level,
    bool IsBuiltIn,
    string? SourceType,
    IReadOnlyList<AdapterPortInfo> Ports,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<string> RequiredSecrets,
    IReadOnlyList<string> Prerequisites,
    ResourceAmounts? MinResources,
    ResourceAmounts? RecommendedResources,
    IReadOnlyList<AdapterSettingGroup> SettingGroups,
    IReadOnlyList<AdapterSetting> Settings)
{
    /// <summary>The group of an L1 adapter's settings, which have no schema; the panel names it.</summary>
    public const string DefaultGroup = "settings";

    public static AdapterSummary From(AdapterDefinition adapter)
    {
        var required = LaunchSpecBuilder.Required(adapter);
        var defaults = LaunchSpecBuilder.DefaultPorts(adapter);
        IReadOnlyList<AdapterSettingGroup> groups;
        IReadOnlyList<AdapterSetting> settings;
        if (adapter.Schema is { } schema)
        {
            var help = adapter.Help ?? AdapterHelp.Empty;
            groups =
            [
                .. schema.Groups.Select(group =>
                    new AdapterSettingGroup(group.Id, group.Label, group.Help, group.Advanced, help.Has(AdapterHelp.GroupPrefix + group.Id))),
            ];
            settings = [.. schema.Settings.Select(setting => AdapterSetting.From(setting, required, help.Has(setting.Key)))];
        }
        else
        {
            groups = [new AdapterSettingGroup(DefaultGroup, null, null, false, false)];
            settings =
            [
                .. required.Settings.Select(key => AdapterSetting.Plain(key, "string")),
                .. required.Secrets.Select(key => AdapterSetting.Plain(key, "secret")),
            ];
        }

        return new AdapterSummary(
            adapter.Id,
            adapter.Name,
            adapter.Version,
            adapter.Level,
            adapter.IsBuiltIn,
            adapter.Source?.Type,
            [.. adapter.Ports.Select(port => new AdapterPortInfo(port.Name, port.Protocol, defaults.GetValueOrDefault(port.Name)))],
            required.Settings,
            required.Secrets,
            adapter.Prerequisites,
            adapter.Resources?.Min,
            adapter.Resources?.Recommended,
            groups,
            settings);
    }
}

/// <summary>
/// A section of the settings form (04 §5); <c>label</c> and <c>help</c> are text per language (<c>en</c>, <c>vi</c>).
/// <c>HasHelp</c>: the adapter's <c>help.json</c> has the topic <c>group:&lt;id&gt;</c> (04 §5.3).
/// </summary>
public sealed record AdapterSettingGroup(
    string Id,
    IReadOnlyDictionary<string, string>? Label,
    IReadOnlyDictionary<string, string>? Help,
    bool Advanced,
    bool HasHelp);

/// <summary>
/// One field of the settings form (04 §5.1). <c>Required</c>: the Owner must give a value before a start; <c>Wizard</c>:
/// the new server wizard asks it. <c>Default</c> is setting text (<c>true</c>, <c>600</c>, <c>normal</c>).
/// <c>HasHelp</c>: the adapter's <c>help.json</c> has a topic for the key (04 §5.3), fetched with
/// <c>GET /adapters/{id}/help</c>.
/// </summary>
public sealed record AdapterSetting(
    string Key,
    string Type,
    string Group,
    IReadOnlyDictionary<string, string>? Label,
    IReadOnlyDictionary<string, string>? Help,
    IReadOnlyDictionary<string, string>? Warning,
    string? Default,
    bool Required,
    bool Wizard,
    int? Min,
    int? Max,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    IReadOnlyList<AdapterSettingOption> Options,
    bool RestartRequired,
    bool HasHelp)
{
    public static AdapterSetting From(SettingDefinition setting, RequiredValues required, bool hasHelp)
    {
        var isRequired = (setting.Type == "secret" ? required.Secrets : required.Settings).Contains(setting.Key);
        return new AdapterSetting(
            setting.Key,
            setting.Type,
            setting.Group,
            setting.Label,
            setting.Help,
            setting.Warning,
            setting.DefaultText,
            isRequired,
            isRequired || setting.Wizard,
            setting.Min,
            setting.Max,
            setting.MinLength,
            setting.MaxLength,
            setting.Pattern,
            [.. setting.Options.Select(option => new AdapterSettingOption(option.Value, option.Label))],
            setting.RestartRequired,
            hasHelp);
    }

    /// <summary>A setting or secret of an L1 adapter: named by its key, required, asked in the wizard.</summary>
    public static AdapterSetting Plain(string key, string type) =>
        new(key, type, AdapterSummary.DefaultGroup, null, null, null, null, true, true, null, null, null, null, null, [], true, false);
}

public sealed record AdapterSettingOption(string Value, IReadOnlyDictionary<string, string>? Label);

public sealed record AdapterPortInfo(string Name, string Protocol, int DefaultPort);

public sealed record AdapterList(IReadOnlyList<AdapterSummary> Adapters, IReadOnlyList<AdapterProblem> Problems);

public sealed record AdapterReloadResult(int Adapters, IReadOnlyList<AdapterProblem> Problems);

/// <summary>Answer to start, stop, restart and console commands: what happened, and the instance afterwards.</summary>
public sealed record InstanceCommandResponse(string Message, InstanceSnapshot Instance);

public sealed record InstanceDeleted(string Deleted, string? Leftovers);

public sealed record ConsoleTail(IReadOnlyList<OutputLine> Lines);

/// <summary>A line of the activity feed (docs/05-api.md §2 "Jobs and activity").</summary>
public sealed record ActivityItem(
    long Id,
    DateTimeOffset At,
    string Kind,
    string Level,
    string? InstanceId,
    string? JobId,
    string? User,
    string Message)
{
    public static ActivityItem From(ActivityRecord record) =>
        new(record.Id, record.At, record.Kind, record.Level, record.InstanceId, record.JobId, record.User, record.Message);
}

/// <summary>A job as the API shows it (docs/03 §5); the Runner bookkeeping of <see cref="JobRecord"/> stays inside Core.</summary>
public sealed record JobInfo(
    string Id,
    string Type,
    string? InstanceId,
    JobState State,
    string? Stage,
    double? Progress,
    string? Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt)
{
    public static JobInfo From(JobRecord job) =>
        new(job.Id, job.Type, job.InstanceId, job.State, job.Stage, job.Progress, job.Message, job.CreatedAt, job.FinishedAt);
}

public sealed record JobStarted(string JobId, JobInfo Job);

public sealed record JobLogTail(IReadOnlyList<string> Lines);
