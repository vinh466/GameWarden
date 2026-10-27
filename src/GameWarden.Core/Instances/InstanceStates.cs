namespace GameWarden.Core.Instances;

/// <summary>What the user asked for; changed only by users and schedules (docs/03-domain-model.md §2).</summary>
public enum DesiredState
{
    Stopped,
    Running,
}

/// <summary>Process state of an instance. A job lock is separate (<c>activeJobId</c>, OQ-15).</summary>
public enum ActualState
{
    NotInstalled,
    Stopped,
    Starting,
    Running,
    Stopping,
    Crashed,
    Faulted,
}

public enum InstanceEvent
{
    Installed,
    Start,
    Ready,
    StartFailed,
    Stop,
    StopFinished,
    ExitedUnexpectedly,
    BackoffElapsed,
    RestartLimitReached,
}

/// <summary>The transitions of docs/03-domain-model.md §3, as one pure function.</summary>
public static class InstanceStateMachine
{
    public static bool TryTransition(ActualState from, InstanceEvent @event, out ActualState to)
    {
        ActualState? next = (from, @event) switch
        {
            (ActualState.NotInstalled, InstanceEvent.Installed) => ActualState.Stopped,
            (ActualState.Stopped or ActualState.Faulted, InstanceEvent.Start) => ActualState.Starting,
            (ActualState.Starting, InstanceEvent.Ready) => ActualState.Running,
            (ActualState.Starting, InstanceEvent.StartFailed) => ActualState.Crashed,
            (ActualState.Starting or ActualState.Running, InstanceEvent.Stop) => ActualState.Stopping,
            (ActualState.Crashed or ActualState.Faulted, InstanceEvent.Stop) => ActualState.Stopped,
            (ActualState.Stopping, InstanceEvent.StopFinished) => ActualState.Stopped,
            (ActualState.Starting or ActualState.Running, InstanceEvent.ExitedUnexpectedly) => ActualState.Crashed,
            (ActualState.Crashed, InstanceEvent.BackoffElapsed) => ActualState.Starting,
            (ActualState.Crashed, InstanceEvent.RestartLimitReached) => ActualState.Faulted,
            _ => null,
        };
        to = next ?? from;
        return next is not null;
    }

    public static ActualState Transition(ActualState from, InstanceEvent @event) =>
        TryTransition(from, @event, out var to)
            ? to
            : throw new InvalidOperationException($"{@event} is not allowed in state {from}.");
}
