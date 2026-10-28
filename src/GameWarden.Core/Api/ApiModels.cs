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
    ResourceAmounts? RecommendedResources)
{
    public static AdapterSummary From(AdapterDefinition adapter)
    {
        var required = LaunchSpecBuilder.Required(adapter);
        var defaults = LaunchSpecBuilder.DefaultPorts(adapter);
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
            adapter.Resources?.Recommended);
    }
}

public sealed record AdapterPortInfo(string Name, string Protocol, int DefaultPort);

public sealed record AdapterList(IReadOnlyList<AdapterSummary> Adapters, IReadOnlyList<AdapterProblem> Problems);

public sealed record AdapterReloadResult(int Adapters, IReadOnlyList<AdapterProblem> Problems);

/// <summary>Answer to start, stop, restart and console commands: what happened, and the instance afterwards.</summary>
public sealed record InstanceCommandResponse(string Message, InstanceSnapshot Instance);

public sealed record InstanceDeleted(string Deleted, string? Leftovers);

public sealed record ConsoleTail(IReadOnlyList<OutputLine> Lines);

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
