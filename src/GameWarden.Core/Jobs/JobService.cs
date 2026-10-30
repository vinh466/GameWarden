using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using GameWarden.Core.Adapters;
using GameWarden.Core.Checks;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Processes;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Jobs;

public sealed record JobStartResult(JobRecord? Job, string? Error, string? Code, int StatusCode);

/// <summary>
/// Install and update jobs (03 §5). A job holds the instance lock for its whole run and the global SteamCMD lock
/// while SteamCMD runs (OQ-22). An update stops a running game through the normal stop path and starts it again
/// afterwards if its desired state is Running. Pre-update backups arrive with backups (backlog, ADR-0018).
/// </summary>
public sealed class JobService(
    SupervisorContext context,
    Supervisor supervisor,
    SteamCmd steamCmd,
    SteamCmdPolicy policy,
    ILogger<JobService> logger)
{
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _steamCmdLock = new(1, 1);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);

    /// <summary>Core start-up: jobs a previous Core left running failed; their tools are ended.</summary>
    public async Task FailInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        var interrupted = await db.Jobs.Where(job => job.State == JobState.Queued || job.State == JobState.Running).ToListAsync(cancellationToken);
        foreach (var job in interrupted)
        {
            if (job.RunnerPid is { } pid && context.ProcessHost.IsAlive(pid, job.RunnerStartedAt))
            {
                context.ProcessHost.Kill(pid);
            }

            job.State = JobState.Failed;
            job.Message = "Core restarted while this job was running; run it again.";
            job.FinishedAt = context.Time.GetUtcNow();
            logger.LogWarning("Job {Job} ({Type} of {Instance}) was interrupted by a Core restart.", job.Id, job.Type, job.InstanceId);
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var job in interrupted)
        {
            context.Events.RaiseJobChanged(job);
        }

        JobLog.Prune(context.Options.JobLogsPath, context.Time.GetUtcNow());
    }

    /// <param name="update">Update an installed game (no validate) instead of installing or repairing it.</param>
    /// <param name="force">Install even with less free disk than the adapter needs (OQ-07).</param>
    public async Task<JobStartResult> StartInstallAsync(string instanceId, bool update, bool force, CancellationToken cancellationToken)
    {
        if (supervisor.Find(instanceId) is not { } actor)
        {
            return new JobStartResult(null, $"No instance '{instanceId}'.", "not_found", StatusCodes.Status404NotFound);
        }

        var adapter = context.Adapters.Find(actor.Snapshot.AdapterId);
        if (adapter?.Source is not { Type: "steamcmd", AppId: { } appId })
        {
            return new JobStartResult(null, $"Adapter '{actor.Snapshot.AdapterId}' is not installed with SteamCMD; put its files in {actor.ServerFolder} by hand.", ProblemCodes.InvalidState, StatusCodes.Status409Conflict);
        }

        if (update && actor.Snapshot.State == ActualState.NotInstalled)
        {
            return new JobStartResult(null, $"The game is not installed yet. Run: gamewarden install {instanceId}", ProblemCodes.InvalidState, StatusCodes.Status409Conflict);
        }

        var disk = MachineResources.Shortfalls(adapter.Resources?.Min, context.Machine.Snapshot(context.Options.DataRoot), forInstall: true);
        if (!update && disk.Count > 0 && !force)
        {
            return new JobStartResult(null, $"Not enough disk for {adapter.Name}: needs {string.Join(", ", disk)}. Free some space, or install anyway with --force.", ProblemCodes.ResourcesInsufficient, StatusCodes.Status412PreconditionFailed);
        }

        var job = new JobRecord
        {
            Id = NewJobId(),
            Type = update ? "update" : "install",
            InstanceId = instanceId,
            State = JobState.Queued,
            CreatedAt = context.Time.GetUtcNow(),
        };
        var locked = await actor.BeginJobAsync(job.Id);
        if (locked.Outcome == CommandOutcome.Rejected)
        {
            return new JobStartResult(null, locked.Message, locked.Code, StatusCodes.Status409Conflict);
        }

        await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
        {
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);
        }

        context.Events.RaiseJobChanged(job);

        _running[job.Id] = Task.Run(() => RunInstallAsync(job, actor, adapter, appId, update));
        logger.LogInformation("Job {Job}: {Type} of {Instance} queued.", job.Id, job.Type, LogText.Clean(instanceId));
        return new JobStartResult(job, null, null, StatusCodes.Status202Accepted);
    }

    public async Task<IReadOnlyList<JobRecord>> ListAsync(string? instanceId, CancellationToken cancellationToken)
    {
        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        var jobs = await db.Jobs.AsNoTracking().Where(job => instanceId == null || job.InstanceId == instanceId).ToListAsync(cancellationToken);
        return [.. jobs.OrderByDescending(job => job.CreatedAt).Take(50)];
    }

    public async Task<JobRecord?> FindAsync(string jobId, CancellationToken cancellationToken)
    {
        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        return await db.Jobs.AsNoTracking().FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);
    }

    /// <summary>True while any job runs; updates wait for none (10 §6).</summary>
    public bool AnyRunning => !_running.IsEmpty;

    /// <summary>Waits for a job started by this Core (tests and <c>--wait</c> use the API instead).</summary>
    public Task WaitAsync(string jobId) => _running.GetValueOrDefault(jobId) ?? Task.CompletedTask;

    private async Task RunInstallAsync(JobRecord job, InstanceActor actor, AdapterDefinition adapter, int appId, bool update)
    {
        using var log = new JobLog(context.Options.JobLogsPath, job.Id);
        var progressSavedAt = DateTimeOffset.MinValue;
        var installed = false;
        string? problem = null;
        try
        {
            log.Write($"{job.Type} of {job.InstanceId} ({adapter.Name}, Steam app {appId}).");
            if (actor.Snapshot.State is ActualState.Starting or ActualState.Running or ActualState.Crashed)
            {
                await SetStageAsync(job, "stop", log);
                await actor.StopForJobAsync();
                log.Write($"Stopped; state {actor.Snapshot.State}.");
            }

            await SetStageAsync(job, "waiting for SteamCMD", log);
            await _steamCmdLock.WaitAsync();
            try
            {
                await SetStageAsync(job, "steamcmd.prepare", log);
                await steamCmd.EnsureReadyAsync(job.Id, log, runner => RecordRunner(job, runner), CancellationToken.None);

                await SetStageAsync(job, update ? "steamcmd.update" : "steamcmd.install", log);
                context.DataRoot.PrepareInstance(InstancePaths.For(context.Options.DataRoot, job.InstanceId!));
                SteamCmdResult? result = null;
                for (var attempt = 1; attempt <= policy.Attempts; attempt++)
                {
                    log.Write($"SteamCMD app_update, try {attempt} of {policy.Attempts}.");
                    result = await steamCmd.AppUpdateAsync(
                        $"{job.Id}-{attempt}",
                        appId,
                        actor.ServerFolder,
                        validate: !update,
                        log,
                        percent =>
                        {
                            job.Progress = Math.Round(percent, 1);
                            var now = context.Time.GetUtcNow();
                            if (now - progressSavedAt >= ProgressSaveInterval)
                            {
                                progressSavedAt = now;
                                _ = SaveAsync(job);
                            }
                        },
                        runner => RecordRunner(job, runner),
                        CancellationToken.None);
                    if (result.Success || !result.Retryable || attempt == policy.Attempts)
                    {
                        break;
                    }

                    var delay = policy.RetryDelays[attempt - 1];
                    log.Write($"{result.Message} Trying again in {delay.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s.");
                    await Task.Delay(delay, context.Time);
                }

                installed = result!.Success;
                problem = installed ? null : $"{job.Type} failed: {result.Message}";
            }
            finally
            {
                _steamCmdLock.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or HttpRequestException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            logger.LogError(ex, "Job {Job} failed.", job.Id);
            problem = $"{job.Type} failed: {ex.Message}";
        }

        try
        {
            if (installed)
            {
                await SetStageAsync(job, "start", log);
            }

            var started = await actor.EndJobAsync(job.Id, installed, startIfDesired: true, problem);
            if (started is { Outcome: CommandOutcome.Rejected })
            {
                log.Write($"Start after the {job.Type} was refused: {started.Message}");
            }

            job.State = installed ? JobState.Succeeded : JobState.Failed;
            job.Message = problem ?? (started is null ? "Installed." : "Installed; the server is starting again.");
            job.Progress = installed ? 100 : job.Progress;
            job.RunnerPid = null;
            job.FinishedAt = context.Time.GetUtcNow();
            log.Write(job.Message);
            await SaveAsync(job);
            logger.LogInformation("Job {Job}: {State}. {Message}", job.Id, job.State, job.Message);
        }
        finally
        {
            _running.TryRemove(job.Id, out _);
        }
    }

    private async Task SetStageAsync(JobRecord job, string stage, JobLog log)
    {
        job.State = JobState.Running;
        job.Stage = stage;
        job.Progress = null;
        log.Write($"Stage: {stage}");
        await SaveAsync(job);
    }

    private void RecordRunner(JobRecord job, RunnerProcess runner)
    {
        job.RunnerPid = runner.Pid;
        job.RunnerStartedAt = runner.StartedAt;
        _ = SaveAsync(job);
    }

    /// <summary>
    /// Saves are serialized and copy the job when they run, so a late progress save never writes an older state. Mods jobs
    /// (<see cref="Mods.ModsService"/>) save through here too.
    /// </summary>
    internal async Task SaveAsync(JobRecord job)
    {
        await _saveLock.WaitAsync();
        try
        {
            var copy = new JobRecord
            {
                Id = job.Id,
                Type = job.Type,
                InstanceId = job.InstanceId,
                State = job.State,
                Stage = job.Stage,
                Progress = job.Progress,
                Message = job.Message,
                RunnerPid = job.RunnerPid,
                RunnerStartedAt = job.RunnerStartedAt,
                CreatedAt = job.CreatedAt,
                FinishedAt = job.FinishedAt,
            };
            await using var db = await context.Database.CreateDbContextAsync();
            db.Jobs.Update(copy);
            await db.SaveChangesAsync();
            context.Events.RaiseJobChanged(copy);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Could not save job {Job}.", job.Id);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    internal string NewJobId() =>
        $"{context.Time.GetUtcNow():yyyyMMdd-HHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}";
}
