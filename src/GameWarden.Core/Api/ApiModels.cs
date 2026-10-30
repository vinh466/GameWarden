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

    /// <summary>The adapter's mod loader (L3, docs/13 §4.1); null when it has no mods. Not a constructor parameter, so the OpenAPI schema lists it as optional.</summary>
    public string? ModLoader { get; init; }

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
            settings)
        {
            ModLoader = adapter.Mods?.Loader,
        };
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

/// <summary>
/// Body of <c>PUT /instances/{id}/mods</c>, one of two (docs/13 §4.4, §4.5): <c>entries</c>, the whole list of chosen
/// mods, or <c>previewId</c>, a code preview to apply as it was shown. Core adds the dependencies either way.
/// </summary>
public sealed record ModsUpdate(IReadOnlyList<ModEntryRequest>? Entries = null, string? PreviewId = null);

/// <param name="Package"><c>Namespace-Name</c> on Thunderstore.</param>
/// <param name="Version"><c>x.y.z</c>; the latest when left out.</param>
/// <param name="Enabled">A disabled mod stays in the set and is not installed.</param>
public sealed record ModEntryRequest(string Package, string? Version = null, bool Enabled = true);

/// <summary>Body of <c>PUT /instances/{id}/mods/enabled</c>: <i>Mods on</i> or <i>off</i>, applied at the next start (docs/13 §4.3).</summary>
public sealed record ModsEnabledUpdate(bool Enabled);

/// <summary>
/// An instance's mods (docs/13 §4.2): the active set (used at the next start), the previous one (rollback), and the
/// newest set that is still building or failed, with its job. <c>Enabled</c> is <i>Mods on</i> (§4.3).
/// </summary>
/// <param name="ShareCode">The last r2modman code made from the active set for friends (§4.6), with its time.</param>
/// <param name="NewerVersions">The last update check (§4.8), kept in memory: the active set's packages that have a newer version.</param>
public sealed record ModsView(
    string InstanceId,
    string Loader,
    bool Enabled,
    ModSetView? Active,
    ModSetView? Previous,
    ModSetView? Pending,
    string? ShareCode,
    DateTimeOffset? ShareCodeCreatedAt,
    ModUpdateCheck? NewerVersions);

/// <param name="Origin"><c>code</c>, <c>edit</c>, <c>update</c> or <c>rollback</c>.</param>
public sealed record ModSetView(long Id, DateTimeOffset CreatedAt, string Origin, ModSetState State, string? JobId, IReadOnlyList<ModEntryView> Entries);

/// <param name="Origin"><c>chosen</c> or <c>dependency</c>.</param>
public sealed record ModEntryView(string Package, string Version, bool Enabled, string Origin);

/// <summary>Body of <c>POST /instances/{id}/mods/code-preview</c>: an r2modman profile code (docs/13 §4.4).</summary>
public sealed record ModCodePreviewRequest(string Code);

/// <summary>
/// A code read and checked, nothing changed (docs/13 §4.4): <c>PUT /mods</c> with <c>previewId</c> applies it until
/// <c>expiresAt</c>. <c>Warnings</c> name what was left out; <c>ConfigFiles</c> are the paths under
/// <c>BepInEx/config</c> that the set gets at its first start (§4.7).
/// </summary>
public sealed record ModCodePreview(
    string PreviewId,
    string Code,
    string ProfileName,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ModCodePreviewEntry> Entries,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ConfigFiles);

/// <param name="Version">As the code has it; it never changes (docs/13 §4.5).</param>
/// <param name="Enabled">As the code has it; a disabled mod stays in the set and is not installed.</param>
/// <param name="State">Thunderstore's state of the package.</param>
public sealed record ModCodePreviewEntry(string Package, string Version, bool Enabled, ModPackageState State);

public enum ModPackageState
{
    Ok,
    Deprecated,
    NotFound,
}

/// <summary>An r2modman code of the active set for friends (docs/13 §4.6); it carries no config files.</summary>
public sealed record ModShareCode(string Code, DateTimeOffset CreatedAt);

/// <summary>An update check (docs/13 §4.8): the packages of the active set that have a newer version on Thunderstore.</summary>
public sealed record ModUpdateCheck(DateTimeOffset CheckedAt, IReadOnlyList<ModUpdateInfo> Updates);

public sealed record ModUpdateInfo(string Package, string Current, string Latest);

/// <summary>The config files of the active set (docs/13 §4.7), subfolders included; <c>SetId</c> is null without an active set.</summary>
public sealed record ModConfigList(long? SetId, IReadOnlyList<ModConfigFileInfo> Files);

/// <param name="Path">Relative to the set's config folder, with <c>/</c> separators.</param>
public sealed record ModConfigFileInfo(string Path, long Size, DateTimeOffset Modified);

/// <summary>One config file's text (UTF-8), at most 1 MB.</summary>
public sealed record ModConfigContent(long SetId, string Path, long Size, DateTimeOffset Modified, string Text);
