using GameWarden.Core.Instances;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class InstanceStateMachineTests
{
    [Theory]
    [InlineData(ActualState.NotInstalled, InstanceEvent.Installed, ActualState.Stopped)]
    [InlineData(ActualState.Stopped, InstanceEvent.Start, ActualState.Starting)]
    [InlineData(ActualState.Faulted, InstanceEvent.Start, ActualState.Starting)]
    [InlineData(ActualState.Starting, InstanceEvent.Ready, ActualState.Running)]
    [InlineData(ActualState.Starting, InstanceEvent.StartFailed, ActualState.Crashed)]
    [InlineData(ActualState.Running, InstanceEvent.Stop, ActualState.Stopping)]
    [InlineData(ActualState.Starting, InstanceEvent.Stop, ActualState.Stopping)]
    [InlineData(ActualState.Crashed, InstanceEvent.Stop, ActualState.Stopped)]
    [InlineData(ActualState.Faulted, InstanceEvent.Stop, ActualState.Stopped)]
    [InlineData(ActualState.Stopping, InstanceEvent.StopFinished, ActualState.Stopped)]
    [InlineData(ActualState.Running, InstanceEvent.ExitedUnexpectedly, ActualState.Crashed)]
    [InlineData(ActualState.Crashed, InstanceEvent.BackoffElapsed, ActualState.Starting)]
    [InlineData(ActualState.Crashed, InstanceEvent.RestartLimitReached, ActualState.Faulted)]
    public void Allowed_transitions(ActualState from, InstanceEvent @event, ActualState expected)
    {
        Assert.Equal(expected, InstanceStateMachine.Transition(from, @event));
    }

    [Theory]
    [InlineData(ActualState.NotInstalled, InstanceEvent.Start)]
    [InlineData(ActualState.Running, InstanceEvent.Start)]
    [InlineData(ActualState.Stopped, InstanceEvent.Ready)]
    [InlineData(ActualState.Stopping, InstanceEvent.Start)]
    [InlineData(ActualState.Faulted, InstanceEvent.BackoffElapsed)]
    public void Other_transitions_are_refused(ActualState from, InstanceEvent @event)
    {
        Assert.False(InstanceStateMachine.TryTransition(from, @event, out var to));
        Assert.Equal(from, to);
    }
}
