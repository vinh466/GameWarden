using GameWarden.Core.Instances;

namespace GameWarden.Core.Supervision;

/// <summary>Immutable view of one instance, safe to read from any thread.</summary>
public sealed record InstanceSnapshot(
    string Id,
    string DisplayName,
    string AdapterId,
    DesiredState DesiredState,
    ActualState State,
    string? ActiveJobId,
    int? RunnerPid,
    int? GamePid,
    int RecentRestarts,
    string? LastProblem,
    IReadOnlyDictionary<string, int> Ports);

public enum CommandOutcome
{
    Accepted,
    NothingToDo,
    Rejected,
}

/// <summary>Stable error codes of docs/05-api.md §5.</summary>
public static class ProblemCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InstanceBusy = "instance_busy";
    public const string InvalidState = "invalid_state";
    public const string PortConflict = "port_conflict";
    public const string PrerequisiteMissing = "prerequisite_missing";
    public const string ResourcesInsufficient = "resources_insufficient";
}

public sealed record CommandResult(CommandOutcome Outcome, string Message, InstanceSnapshot Instance, string? Code = null)
{
    public static CommandResult Accepted(InstanceSnapshot instance, string message = "Accepted.") => new(CommandOutcome.Accepted, message, instance);

    public static CommandResult NothingToDo(InstanceSnapshot instance, string message) => new(CommandOutcome.NothingToDo, message, instance);

    public static CommandResult Rejected(InstanceSnapshot instance, string message, string code = ProblemCodes.InvalidState) =>
        new(CommandOutcome.Rejected, message, instance, code);
}
