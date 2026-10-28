using GameWarden.Contracts.Runner;
using GameWarden.Core.Data;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Events;

/// <summary>
/// Core's in-process event bus (docs/03-domain-model.md §6): instances, jobs and the activity log say what happened;
/// the realtime hub and the activity log listen. Handlers run on the raiser's thread (an instance's queue, a job), so
/// they only hand the event on and never wait.
/// </summary>
public sealed class CoreEvents(ILogger<CoreEvents> logger)
{
    /// <summary>An instance was created (<c>before</c> is null) or something visible about it changed.</summary>
    public event Action<InstanceSnapshot?, InstanceSnapshot>? InstanceChanged;

    public event Action<string>? InstanceRemoved;

    public event Action<string, OutputLine>? ConsoleLine;

    /// <summary>A job was created, moved to a new stage or progress, or finished.</summary>
    public event Action<JobRecord>? JobChanged;

    public event Action<ActivityRecord>? ActivityAdded;

    public void RaiseInstanceChanged(InstanceSnapshot? before, InstanceSnapshot after) => Raise(InstanceChanged, handler => handler(before, after));

    public void RaiseInstanceRemoved(string instanceId) => Raise(InstanceRemoved, handler => handler(instanceId));

    public void RaiseConsoleLine(string instanceId, OutputLine line) => Raise(ConsoleLine, handler => handler(instanceId, line));

    public void RaiseJobChanged(JobRecord job) => Raise(JobChanged, handler => handler(job));

    public void RaiseActivityAdded(ActivityRecord activity) => Raise(ActivityAdded, handler => handler(activity));

    /// <summary>A failing listener must not break the instance or job that raised the event, nor the other listeners.</summary>
    private void Raise<T>(T? handlers, Action<T> invoke)
        where T : Delegate
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<T>())
        {
            try
            {
                invoke(handler);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An event listener failed.");
            }
        }
    }
}
