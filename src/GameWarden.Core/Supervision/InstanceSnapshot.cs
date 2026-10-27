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
    string? LastProblem);

public enum CommandOutcome
{
    Accepted,
    NothingToDo,
    Rejected,
}

public sealed record CommandResult(CommandOutcome Outcome, string Message, InstanceSnapshot Instance)
{
    public static CommandResult Accepted(InstanceSnapshot instance, string message = "Accepted.") => new(CommandOutcome.Accepted, message, instance);

    public static CommandResult NothingToDo(InstanceSnapshot instance, string message) => new(CommandOutcome.NothingToDo, message, instance);

    public static CommandResult Rejected(InstanceSnapshot instance, string message) => new(CommandOutcome.Rejected, message, instance);
}
