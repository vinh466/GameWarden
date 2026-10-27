using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using GameWarden.Contracts;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Adapters;
using GameWarden.Core.Checks;
using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Network;
using GameWarden.Core.Processes;
using GameWarden.Core.Secrets;

namespace GameWarden.Core.Supervision;

/// <summary>
/// Owns one instance. Every command and event for it — user actions, Runner events, timers, reconciliation —
/// runs on this actor's queue one at a time, so they cannot race (OQ-25). Nothing else changes the instance.
/// </summary>
public sealed class InstanceActor : IAsyncDisposable
{
    private readonly SupervisorContext _context;
    private readonly ILogger _logger;
    private readonly Channel<Func<Task>> _mailbox = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly RestartTracker _restarts;
    private readonly Task _loop;
    private readonly InstanceRecord _record;
    private ActualState _state;
    private RunnerClient? _runner;
    private int? _gamePid;
    private bool _stopRequested;
    private bool _restartAfterStop;
    private DateTimeOffset? _runningSince;
    private CancellationTokenSource? _phase; // readiness, back-off or stop timeout: one at a time
    private Regex? _readyPattern;
    private string? _lastProblem;
    private string? _activeJobId; // the instance lock (OQ-15)
    private bool _shuttingDown; // Windows is shutting down: stop, keep the desired state, never restart
    private bool _retired; // being deleted
    private readonly List<TaskCompletionSource> _settledWaiters = [];
    private volatile InstanceSnapshot _snapshot;

    public InstanceActor(InstanceRecord record, SupervisorContext context)
    {
        _record = record;
        _context = context;
        _logger = context.LoggerFactory.CreateLogger($"GameWarden.Instance.{record.Id}");
        _restarts = new RestartTracker(context.RestartPolicy);
        _state = record.PersistedState;
        _snapshot = BuildSnapshot();
        _loop = Task.Run(RunLoopAsync);
    }

    public string Id => _record.Id;

    public InstanceSnapshot Snapshot => _snapshot;

    private InstancePaths Paths => InstancePaths.For(_context.Options.DataRoot, Id);

    /// <summary>Core start: reattach to a live Runner, or bring the instance back to its desired state.</summary>
    public Task InitializeAsync() => Run(ReconcileOnStartAsync);

    /// <param name="force">Start even below the adapter's minimum resources (Owner only, OQ-07).</param>
    public Task<CommandResult> StartAsync(bool force = false) => Ask(() => StartCoreAsync(force));

    public Task<CommandResult> StopAsync() => Ask(StopCoreAsync);

    public Task<CommandResult> RestartAsync(bool force = false) => Ask(() => RestartCoreAsync(force));

    public Task<CommandResult> SendConsoleAsync(string command) => Ask(() => SendConsoleCoreAsync(command));

    public Task<IReadOnlyList<OutputLine>> TailAsync(int lines) => Ask(() => TailCoreAsync(lines));

    /// <summary>Core is going away (service stop, update): let go of the Runner and keep the game running (ADR-0003).</summary>
    public Task DetachAsync() => Run(DetachCoreAsync);

    /// <summary>Windows is shutting down (ADR-0014): stop the game gracefully, keep the desired state so it comes back after boot.</summary>
    public async Task StopForShutdownAsync()
    {
        await Run(StopForShutdownCoreAsync);
        await WaitUntilSettledAsync();
    }

    /// <summary>Completes once the instance is neither starting, running nor stopping.</summary>
    public Task WaitUntilSettledAsync() => Ask(() =>
    {
        if (IsSettled)
        {
            return Task.FromResult(Task.CompletedTask);
        }

        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _settledWaiters.Add(waiter);
        return Task.FromResult(waiter.Task);
    }).Unwrap();

    public Task<SettingsView> GetSettingsAsync() => Ask(() => Task.FromResult(BuildSettingsView()));

    public string ServerFolder => Paths.Server;

    /// <summary>Merges <paramref name="changes"/> (a null value removes the setting); applies on the next start.</summary>
    public Task<SettingsView> UpdateSettingsAsync(IReadOnlyDictionary<string, string?> changes) => Ask(async () =>
    {
        var settings = Settings().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var (key, value) in changes)
        {
            if (value is null)
            {
                settings.Remove(key);
            }
            else
            {
                settings[key] = value;
            }
        }

        _record.SettingsJson = JsonSerializer.Serialize(settings, ContractJson.Options);
        await SaveAsync();
        return BuildSettingsView();
    });

    /// <summary>
    /// Takes the instance lock for a job (OQ-15). While it is held, user starts are refused (<c>instance_busy</c>)
    /// and crashes are not restarted; the job drives the state through the normal transitions (03 §2).
    /// </summary>
    public Task<CommandResult> BeginJobAsync(string jobId) => Ask(() =>
    {
        if (_activeJobId is not null)
        {
            return Task.FromResult(CommandResult.Rejected(BuildSnapshot(), $"Job {_activeJobId} is running on this instance.", ProblemCodes.InstanceBusy));
        }

        if (_retired || _state == ActualState.Stopping)
        {
            return Task.FromResult(CommandResult.Rejected(BuildSnapshot(), _retired ? "The instance is being deleted." : "The instance is stopping."));
        }

        _activeJobId = jobId;
        return Task.FromResult(CommandResult.Accepted(BuildSnapshot()));
    });

    /// <summary>A job that needs the game stopped (e.g. update) stops it through the normal stop path; the desired state stays.</summary>
    public async Task StopForJobAsync()
    {
        await Run(async () =>
        {
            CancelPhase(); // also drops a pending automatic restart
            switch (_state)
            {
                case ActualState.Starting or ActualState.Running:
                    _restartAfterStop = false;
                    await BeginStopAsync();
                    break;
                case ActualState.Crashed:
                    Apply(InstanceEvent.Stop);
                    await SaveAsync();
                    break;
            }
        });
        await WaitUntilSettledAsync();
    }

    /// <summary>
    /// Releases the instance lock. A successful install moves NotInstalled to Stopped; then, with
    /// <paramref name="startIfDesired"/>, the game starts again if its desired state is Running (03 §5).
    /// </summary>
    public Task<CommandResult?> EndJobAsync(string jobId, bool installed, bool startIfDesired, string? problem = null) => Ask<CommandResult?>(async () =>
    {
        if (_activeJobId != jobId)
        {
            return null;
        }

        _activeJobId = null;
        if (installed && _state == ActualState.NotInstalled)
        {
            Apply(InstanceEvent.Installed);
        }

        _lastProblem = problem ?? _lastProblem;
        await SaveAsync();
        if (startIfDesired && installed && _record.DesiredState == DesiredState.Running && _state is ActualState.Stopped or ActualState.Faulted)
        {
            _restarts.Reset();
            return await LaunchAsync();
        }

        return null;
    });

    /// <summary>Marks a settled instance as being deleted, so nothing starts it again.</summary>
    public Task<CommandResult> RetireAsync() => Ask(() =>
    {
        if (_activeJobId is not null)
        {
            return Task.FromResult(CommandResult.Rejected(BuildSnapshot(), $"Job {_activeJobId} is running on this instance.", ProblemCodes.InstanceBusy));
        }

        if (!IsSettled || _state == ActualState.Crashed || _runner is not null)
        {
            return Task.FromResult(CommandResult.Rejected(BuildSnapshot(), $"The instance is {_state}; stop it first."));
        }

        CancelPhase();
        _retired = true;
        return Task.FromResult(CommandResult.Accepted(BuildSnapshot()));
    });

    public async ValueTask DisposeAsync()
    {
        _mailbox.Writer.TryComplete();
        await _loop;
        CancelPhase();
        if (_runner is not null)
        {
            await _runner.DisposeAsync();
            _runner = null;
        }
    }

    // ----- queue -----------------------------------------------------------------------------

    private Task Run(Func<Task> work) => Ask(async () =>
    {
        await work();
        return true;
    });

    private Task<T> Ask<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = _mailbox.Writer.TryWrite(async () =>
        {
            try
            {
                var result = await work();
                _snapshot = BuildSnapshot(); // callers see the state their command produced
                done.TrySetResult(result);
            }
            catch (Exception ex)
            {
                _snapshot = BuildSnapshot();
                done.TrySetException(ex);
            }
        });
        if (!posted)
        {
            done.TrySetException(new ObjectDisposedException($"instance {Id}"));
        }

        return done.Task;
    }

    private void Post(Func<Task> work) => _mailbox.Writer.TryWrite(work);

    private async Task RunLoopAsync()
    {
        await foreach (var work in _mailbox.Reader.ReadAllAsync())
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in instance {Id}.", Id);
            }
            finally
            {
                _snapshot = BuildSnapshot();
                if (IsSettled && _settledWaiters.Count > 0)
                {
                    foreach (var waiter in _settledWaiters)
                    {
                        waiter.TrySetResult();
                    }

                    _settledWaiters.Clear();
                }
            }
        }

        foreach (var waiter in _settledWaiters)
        {
            waiter.TrySetResult();
        }
    }

    private bool IsSettled => _state is not (ActualState.Starting or ActualState.Running or ActualState.Stopping);

    // ----- commands --------------------------------------------------------------------------

    private async Task<CommandResult> StartCoreAsync(bool force)
    {
        switch (_state)
        {
            case ActualState.Starting or ActualState.Running:
                return CommandResult.NothingToDo(BuildSnapshot(), "Already running.");
            case ActualState.Stopping:
                return CommandResult.Rejected(BuildSnapshot(), "The instance is stopping.");
            case ActualState.NotInstalled:
                return CommandResult.Rejected(BuildSnapshot(), $"The game is not installed. Run: gamewarden install {Id}");
        }

        if (_activeJobId is not null)
        {
            return CommandResult.Rejected(BuildSnapshot(), $"Job {_activeJobId} is running on this instance.", ProblemCodes.InstanceBusy);
        }

        if (_retired)
        {
            return CommandResult.Rejected(BuildSnapshot(), "The instance is being deleted.");
        }

        var (problem, warning) = CheckBeforeStart(force);
        if (problem is not null)
        {
            _logger.LogWarning("{Id}: start refused: {Problem}", Id, problem.Message);
            return problem;
        }

        CancelPhase(); // a pending automatic restart (Crashed) is replaced by this start
        _restarts.Reset(); // a manual start resets the crash counter (03 §4)
        _record.DesiredState = DesiredState.Running;
        await SaveAsync();
        var result = await LaunchAsync();
        return warning is null || result.Outcome != CommandOutcome.Accepted ? result : result with { Message = $"Starting. Warning: {warning}" };
    }

    /// <summary>The checks of 08 §2 before a start: settings and secrets, prerequisites, ports, resources (OQ-07).</summary>
    private (CommandResult? Problem, string? Warning) CheckBeforeStart(bool force)
    {
        var adapter = _context.Adapters.Find(_record.AdapterId);
        if (adapter is null)
        {
            return (CommandResult.Rejected(BuildSnapshot(), $"Adapter '{_record.AdapterId}' is missing or invalid."), null);
        }

        var missing = LaunchSpecBuilder.Required(adapter).DescribeMissing(Id, Settings(), [.. _context.Secrets.Names(SecretScopes.Instance(Id))]);
        if (missing is not null)
        {
            return (CommandResult.Rejected(BuildSnapshot(), missing, ProblemCodes.ValidationFailed), null);
        }

        var prerequisites = adapter.Prerequisites.Select(Prerequisites.Check).Where(status => !status.Installed).ToArray();
        if (prerequisites.Length > 0)
        {
            return (CommandResult.Rejected(
                BuildSnapshot(),
                "Missing prerequisites: " + string.Join("; ", prerequisites.Select(status => $"{status.Id} — install {status.Hint}")),
                ProblemCodes.PrerequisiteMissing), null);
        }

        var conflicts = Network.Ports.Conflicts(adapter, Ports(), _context.IsPortListening);
        if (conflicts.Count > 0)
        {
            return (CommandResult.Rejected(BuildSnapshot(), $"Another program already uses port {string.Join(", ", conflicts)}.", ProblemCodes.PortConflict), null);
        }

        var machine = MachineResources.Read(_context.Options.DataRoot);
        var belowMinimum = MachineResources.Shortfalls(adapter.Resources?.Min, machine, forInstall: false);
        if (belowMinimum.Count > 0 && !force)
        {
            return (CommandResult.Rejected(
                BuildSnapshot(),
                $"Below the minimum for {adapter.Name}: needs {string.Join(", ", belowMinimum)}. Add resources to the VM, or start anyway with --force.",
                ProblemCodes.ResourcesInsufficient), null);
        }

        var belowRecommended = MachineResources.Shortfalls(adapter.Resources?.Recommended, machine, forInstall: false);
        return (null, belowMinimum.Count > 0
            ? $"forced below the minimum ({string.Join(", ", belowMinimum)})"
            : belowRecommended.Count > 0 ? $"below the recommended {string.Join(", ", belowRecommended)}" : null);
    }

    private async Task<CommandResult> StopCoreAsync()
    {
        _record.DesiredState = DesiredState.Stopped;
        _restartAfterStop = false;
        switch (_state)
        {
            case ActualState.Stopped or ActualState.NotInstalled:
                await SaveAsync();
                return CommandResult.NothingToDo(BuildSnapshot(), "Already stopped.");
            case ActualState.Stopping:
                await SaveAsync();
                return CommandResult.NothingToDo(BuildSnapshot(), "Already stopping.");
            case ActualState.Crashed or ActualState.Faulted:
                CancelPhase();
                Apply(InstanceEvent.Stop);
                await SaveAsync();
                return CommandResult.Accepted(BuildSnapshot());
            default:
                await BeginStopAsync();
                await SaveAsync();
                return CommandResult.Accepted(BuildSnapshot());
        }
    }

    private async Task<CommandResult> RestartCoreAsync(bool force)
    {
        switch (_state)
        {
            case ActualState.Running or ActualState.Starting:
                _record.DesiredState = DesiredState.Running;
                _restartAfterStop = true;
                await BeginStopAsync();
                await SaveAsync();
                return CommandResult.Accepted(BuildSnapshot(), "Restarting.");
            case ActualState.Stopping:
                return CommandResult.Rejected(BuildSnapshot(), "The instance is stopping.");
            default:
                return await StartCoreAsync(force);
        }
    }

    private async Task StopForShutdownCoreAsync()
    {
        _shuttingDown = true;
        CancelPhase(); // also drops a pending automatic restart
        switch (_state)
        {
            case ActualState.Starting or ActualState.Running:
                _restartAfterStop = false;
                await BeginStopAsync();
                break;
            case ActualState.Crashed:
                Apply(InstanceEvent.Stop);
                await SaveAsync();
                break;
        }
    }

    private async Task<CommandResult> SendConsoleCoreAsync(string command)
    {
        if (_runner is null || _state is not (ActualState.Running or ActualState.Starting))
        {
            return CommandResult.Rejected(BuildSnapshot(), "The game is not running.");
        }

        try
        {
            await _runner.WriteStdinAsync(command);
            return CommandResult.Accepted(BuildSnapshot());
        }
        catch (Exception ex) when (ex is IOException or RunnerCommandRejectedException or ObjectDisposedException)
        {
            return CommandResult.Rejected(BuildSnapshot(), ex.Message);
        }
    }

    private async Task<IReadOnlyList<OutputLine>> TailCoreAsync(int lines)
    {
        if (_runner is null)
        {
            return [];
        }

        try
        {
            return await _runner.GetTailAsync(lines);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return [];
        }
    }

    private async Task DetachCoreAsync()
    {
        CancelPhase();
        var runner = _runner;
        _runner = null;
        if (runner is not null)
        {
            await runner.DisposeAsync(); // RunnerPid stays in the record for the next Core
        }

        await SaveAsync();
    }

    // ----- lifecycle -------------------------------------------------------------------------

    private async Task ReconcileOnStartAsync()
    {
        if (_record.RunnerPid is { } pid)
        {
            if (_context.ProcessHost.IsAlive(pid, _record.RunnerStartedAt) && await TryConnectAsync(pid))
            {
                var status = _runner!.Welcome.Status;
                _state = ActualState.Running;
                if (status.GameRunning)
                {
                    _runningSince = _context.Time.GetUtcNow();
                    _logger.LogInformation("{Id}: reattached to Runner {Pid}; the game is running.", Id, pid);
                    return;
                }

                // The game ended while Core was away; the Runner kept the exit for us.
                await OnGameExitedAsync(_runner, status.ExitCode ?? -1);
                return;
            }

            var exitCode = ReadExitRecord();
            _logger.LogWarning("{Id}: Runner {Pid} is gone; the game ended while Core was away.", Id, pid);
            _record.RunnerPid = null;
            _record.RunnerStartedAt = null;
            if (_record.DesiredState == DesiredState.Running && _state != ActualState.Faulted)
            {
                _state = ActualState.Running;
                await HandleCrashAsync($"ended while Core was away (exit code {exitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"})");
                return;
            }

            await SaveAsync();
        }

        // After a VM reboot: bring back everything that should run. Faulted stays Faulted (OQ-16).
        if (_record.DesiredState == DesiredState.Running && _state == ActualState.Stopped)
        {
            await LaunchAsync();
        }
    }

    private async Task<CommandResult> LaunchAsync()
    {
        CancelPhase();
        if (_shuttingDown || _retired)
        {
            return CommandResult.Rejected(BuildSnapshot(), _shuttingDown ? "Windows is shutting down." : "The instance is being deleted.");
        }

        var adapter = _context.Adapters.Find(_record.AdapterId);
        if (adapter is null)
        {
            return await LaunchFailedAsync($"Adapter '{_record.AdapterId}' is missing or invalid.");
        }

        var paths = Paths;
        try
        {
            _context.DataRoot.PrepareInstance(paths);
            var spec = LaunchSpecBuilder.Build(adapter, Id, _record.DisplayName, paths, Ports(), Settings(), Secrets(adapter));
            var json = JsonSerializer.Serialize(spec, ContractJson.Options);
            _context.Files.Run(() =>
            {
                Directory.CreateDirectory(paths.RunnerState);
                File.WriteAllText(paths.LaunchSpecFile, json);
                File.Delete(Path.Combine(paths.RunnerState, RunnerExitRecord.FileName));
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return await LaunchFailedAsync(ex.Message);
        }

        Apply(_state == ActualState.Crashed ? InstanceEvent.BackoffElapsed : InstanceEvent.Start);
        RunnerProcess runner;
        try
        {
            runner = _context.ProcessHost.StartRunner(_context.Options.ResolvedRunnerPath, paths.LaunchSpecFile, paths.Server);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            await HandleCrashAsync($"could not start the Runner: {ex.Message}");
            return CommandResult.Rejected(BuildSnapshot(), _lastProblem!);
        }

        _record.RunnerPid = runner.Pid;
        _record.RunnerStartedAt = runner.StartedAt;
        await SaveAsync();

        if (!await TryConnectAsync(runner.Pid))
        {
            _context.ProcessHost.Kill(runner.Pid);
            _record.RunnerPid = null;
            _record.RunnerStartedAt = null;
            await HandleCrashAsync("the Runner did not open its pipe");
            return CommandResult.Rejected(BuildSnapshot(), _lastProblem!);
        }

        await ArmReadinessAsync(adapter, _runner!, paths);
        return CommandResult.Accepted(BuildSnapshot());
    }

    private async Task<CommandResult> LaunchFailedAsync(string problem)
    {
        _lastProblem = problem;
        _logger.LogError("{Id}: cannot start: {Problem}", Id, problem);
        if (_state == ActualState.Crashed)
        {
            // An automatic restart that cannot even begin needs a human.
            Apply(InstanceEvent.RestartLimitReached);
            await SaveAsync();
        }

        return CommandResult.Rejected(BuildSnapshot(), problem);
    }

    private async Task<bool> TryConnectAsync(int runnerPid)
    {
        try
        {
            var client = await RunnerClient.ConnectAsync(Id, runnerPid, _context.RunnerConnectTimeout);
            _runner = client;
            _gamePid = client.Welcome.Status.GamePid;
            _ = Task.Run(() => PumpAsync(client));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException or RunnerCommandRejectedException)
        {
            _logger.LogWarning(ex, "{Id}: cannot connect to Runner {Pid}.", Id, runnerPid);
            return false;
        }
    }

    private async Task PumpAsync(RunnerClient client)
    {
        try
        {
            await foreach (var message in client.Events.ReadAllAsync())
            {
                switch (message)
                {
                    case OutputEvent output:
                        Post(() => OnOutputAsync(client, output.Line));
                        break;
                    case ExitedEvent exited:
                        Post(() => OnGameExitedAsync(client, exited.ExitCode));
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The pipe closed; handled below.
        }

        Post(() => OnRunnerLostAsync(client));
    }

    private Task OnOutputAsync(RunnerClient client, OutputLine line)
    {
        if (client == _runner && _state == ActualState.Starting && line.Stream == OutputStream.Stdout && _readyPattern?.IsMatch(line.Text) == true)
        {
            MarkReady();
        }

        return Task.CompletedTask;
    }

    private async Task OnGameExitedAsync(RunnerClient client, int exitCode)
    {
        if (client != _runner)
        {
            return;
        }

        await ReleaseRunnerAsync(acknowledge: true);
        CancelPhase();
        if (_stopRequested)
        {
            _logger.LogInformation("{Id}: stopped (exit code {ExitCode}).", Id, exitCode);
            await FinishStopAsync();
            return;
        }

        await HandleCrashAsync($"exited with code {exitCode}");
    }

    private async Task OnRunnerLostAsync(RunnerClient client)
    {
        if (client != _runner)
        {
            return;
        }

        var exitCode = ReadExitRecord();
        _logger.LogWarning("{Id}: lost the Runner pipe.", Id);
        await ReleaseRunnerAsync(acknowledge: false);
        CancelPhase();
        if (_stopRequested)
        {
            await FinishStopAsync();
            return;
        }

        await HandleCrashAsync(exitCode is null ? "the Runner died" : $"exited with code {exitCode}");
    }

    private async Task HandleCrashAsync(string reason)
    {
        _lastProblem = reason;
        Apply(_state == ActualState.Starting ? InstanceEvent.StartFailed : InstanceEvent.ExitedUnexpectedly);
        _logger.LogWarning("{Id} crashed: {Reason}", Id, reason);
        if (_record.DesiredState != DesiredState.Running || _shuttingDown || _retired || _activeJobId is not null)
        {
            Apply(InstanceEvent.Stop);
            await SaveAsync();
            return;
        }

        var delay = _restarts.OnCrash(_context.Time.GetUtcNow(), _runningSince);
        _runningSince = null;
        if (delay is null)
        {
            Apply(InstanceEvent.RestartLimitReached);
            _logger.LogError("{Id} is Faulted after {Count} restarts; it needs a human.", Id, _restarts.RecentRestarts);
            await SaveAsync();
            return;
        }

        await SaveAsync();
        _logger.LogInformation("{Id}: restarting in {Delay}.", Id, delay.Value);
        StartPhase(delay.Value, OnBackoffElapsedAsync);
    }

    private async Task OnBackoffElapsedAsync()
    {
        if (_state == ActualState.Crashed && _record.DesiredState == DesiredState.Running)
        {
            await LaunchAsync();
        }
    }

    private async Task BeginStopAsync()
    {
        CancelPhase();
        Apply(InstanceEvent.Stop);
        _stopRequested = true;
        var stop = _context.Adapters.Find(_record.AdapterId)?.Stop ?? new AdapterStop("kill", TimeoutSec: 10);
        if (_runner is null)
        {
            await FinishStopAsync();
            return;
        }

        try
        {
            switch (stop.Method)
            {
                case "ctrl-c":
                    await _runner.SendCtrlCAsync();
                    break;
                case "stdin":
                    await _runner.WriteStdinAsync(stop.Command!);
                    break;
                default:
                    // rcon and rest arrive with the adapters that need them; until then the stop falls back to kill.
                    await _runner.KillAsync();
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or RunnerCommandRejectedException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "{Id}: stop command failed; waiting for the timeout.", Id);
        }

        StartPhase(TimeSpan.FromSeconds(stop.TimeoutSec), OnStopTimeoutAsync);
    }

    private async Task OnStopTimeoutAsync()
    {
        if (_state != ActualState.Stopping || _runner is null)
        {
            return;
        }

        _lastProblem = "did not stop in time and was killed";
        _logger.LogWarning("{Id}: did not stop in time; killing it.", Id);
        try
        {
            await _runner.KillAsync();
        }
        catch (Exception ex) when (ex is IOException or RunnerCommandRejectedException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "{Id}: kill failed.", Id);
        }
    }

    private async Task FinishStopAsync()
    {
        _stopRequested = false;
        Apply(InstanceEvent.StopFinished);
        await SaveAsync();
        if (_restartAfterStop && !_shuttingDown)
        {
            _restartAfterStop = false;
            await LaunchAsync();
        }
    }

    // ----- readiness -------------------------------------------------------------------------

    private async Task ArmReadinessAsync(AdapterDefinition adapter, RunnerClient client, InstancePaths paths)
    {
        var readiness = adapter.Readiness!;
        switch (readiness.Type)
        {
            case "delay":
                StartPhase(TimeSpan.FromSeconds(readiness.DelaySec ?? 0), () =>
                {
                    if (_state == ActualState.Starting)
                    {
                        MarkReady();
                    }

                    return Task.CompletedTask;
                });
                return;

            case "logRegex" when readiness.Source is null or "stdout":
                _readyPattern = new Regex(readiness.Pattern!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                StartPhase(TimeSpan.FromSeconds(readiness.TimeoutSec), OnReadinessTimeoutAsync);
                try
                {
                    // The line may have appeared before Core connected.
                    var tail = await client.GetTailAsync(1000);
                    if (tail.Any(line => line.Stream == OutputStream.Stdout && _readyPattern.IsMatch(line.Text)))
                    {
                        MarkReady();
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    // The exit or loss of the Runner is handled by its own event.
                }

                return;

            case "logRegex":
                StartPhase(TimeSpan.FromSeconds(readiness.TimeoutSec), OnReadinessTimeoutAsync);
                var values = LaunchSpecBuilder.TemplateValues(adapter, Id, _record.DisplayName, paths, Ports(), Settings());
                var file = LaunchSpecBuilder.Render(readiness.Source!["file:".Length..], values);
                var probe = FileProbe(file, new Regex(readiness.Pattern!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                StartPolling(() => _context.Files.Run(probe)); // the game's log is gamewarden-run's file (ADR-0009)
                return;

            default: // port
                StartPhase(TimeSpan.FromSeconds(readiness.TimeoutSec), OnReadinessTimeoutAsync);
                var port = Ports().GetValueOrDefault(readiness.Port ?? string.Empty);
                var protocol = Network.Ports.Protocol(adapter, readiness.Port ?? string.Empty);
                StartPolling(() => _context.IsPortListening(protocol, port));
                return;
        }
    }

    private void MarkReady()
    {
        CancelPhase();
        Apply(InstanceEvent.Ready);
        _readyPattern = null;
        _runningSince = _context.Time.GetUtcNow();
        _lastProblem = null;
    }

    private async Task OnReadinessTimeoutAsync()
    {
        if (_state != ActualState.Starting)
        {
            return;
        }

        _logger.LogWarning("{Id}: not ready in time; killing it.", Id);
        if (_runner is null)
        {
            await HandleCrashAsync("not ready in time");
            return;
        }

        try
        {
            await _runner.KillAsync(); // its exit then takes the crash path
        }
        catch (Exception ex) when (ex is IOException or RunnerCommandRejectedException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "{Id}: kill failed.", Id);
        }
    }

    private void StartPolling(Func<bool> probe)
    {
        var phase = _phase!;
        var token = phase.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                bool ready;
                try
                {
                    ready = probe();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NetworkInformationException or System.Security.SecurityException)
                {
                    ready = false;
                }

                if (ready)
                {
                    Post(() =>
                    {
                        if (_phase == phase && _state == ActualState.Starting)
                        {
                            MarkReady();
                        }

                        return Task.CompletedTask;
                    });
                    return;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _context.Time, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    private Func<bool> FileProbe(string path, Regex pattern)
    {
        long offset = _context.Files.Run(() => File.Exists(path) ? new FileInfo(path).Length : 0);
        return () =>
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < offset)
            {
                offset = 0; // the game started a new log file
            }

            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            offset = stream.Length;
            return pattern.IsMatch(text);
        };
    }

    // ----- helpers ---------------------------------------------------------------------------

    private void StartPhase(TimeSpan delay, Func<Task> onElapsed)
    {
        CancelPhase();
        var phase = new CancellationTokenSource();
        var token = phase.Token;
        _phase = phase;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _context.Time, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Post(async () =>
            {
                if (_phase == phase)
                {
                    _phase = null;
                    await onElapsed();
                }
            });
        });
    }

    private void CancelPhase()
    {
        _phase?.Cancel();
        _phase = null;
    }

    private async Task ReleaseRunnerAsync(bool acknowledge)
    {
        var runner = _runner;
        _runner = null;
        _gamePid = null;
        _readyPattern = null;
        _record.RunnerPid = null;
        _record.RunnerStartedAt = null;
        if (runner is null)
        {
            return;
        }

        if (acknowledge)
        {
            try
            {
                await runner.AckExitAsync();
            }
            catch (Exception ex) when (ex is IOException or RunnerCommandRejectedException or ObjectDisposedException)
            {
                // The Runner quits on its own after its linger time.
            }
        }

        await runner.DisposeAsync();
    }

    private void Apply(InstanceEvent @event)
    {
        if (InstanceStateMachine.TryTransition(_state, @event, out var next))
        {
            _logger.LogInformation("{Id}: {From} -> {To} ({Event})", Id, _state, next, @event);
            _state = next;
        }
        else
        {
            _logger.LogWarning("{Id}: ignored {Event} in state {State}.", Id, @event, _state);
        }
    }

    private async Task SaveAsync()
    {
        _record.PersistedState = _state switch
        {
            ActualState.NotInstalled => ActualState.NotInstalled,
            ActualState.Faulted => ActualState.Faulted,
            _ => ActualState.Stopped,
        };
        await using var db = await _context.Database.CreateDbContextAsync();
        db.Instances.Update(_record);
        await db.SaveChangesAsync();
    }

    private int? ReadExitRecord()
    {
        try
        {
            var path = Path.Combine(Paths.RunnerState, RunnerExitRecord.FileName);
            var json = _context.Files.Run(() => File.Exists(path) ? File.ReadAllText(path) : null);
            return json is null ? null : JsonSerializer.Deserialize<RunnerExitRecord>(json, ContractJson.Options)?.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private IReadOnlyDictionary<string, int> Ports() =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(_record.PortsJson, ContractJson.Options) ?? new Dictionary<string, int>();

    private IReadOnlyDictionary<string, string> Settings() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(_record.SettingsJson, ContractJson.Options) ?? new Dictionary<string, string>();

    /// <summary>Only the secrets the launch section uses are decrypted.</summary>
    private Dictionary<string, string> Secrets(AdapterDefinition adapter)
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in LaunchSpecBuilder.Required(adapter).Secrets)
        {
            if (_context.Secrets.Get(SecretScopes.Instance(Id), name) is { } value)
            {
                secrets[name] = value;
            }
        }

        return secrets;
    }

    private SettingsView BuildSettingsView()
    {
        var required = _context.Adapters.Find(_record.AdapterId) is { } adapter
            ? LaunchSpecBuilder.Required(adapter)
            : new RequiredValues([], []);
        var secretsSet = _context.Secrets.Names(SecretScopes.Instance(Id));
        return new SettingsView(
            Settings(),
            required.Settings,
            required.Secrets.Union(secretsSet).Order(StringComparer.Ordinal).ToDictionary(name => name, name => new SecretState(secretsSet.Contains(name))),
            RestartRequired: _state is ActualState.Starting or ActualState.Running or ActualState.Stopping);
    }

    private InstanceSnapshot BuildSnapshot() => new(
        _record.Id,
        _record.DisplayName,
        _record.AdapterId,
        _record.DesiredState,
        _state,
        _activeJobId,
        _record.RunnerPid,
        _gamePid,
        _restarts.RecentRestarts,
        _lastProblem,
        Ports());
}

/// <summary>Settings of an instance as the API shows them: secrets only as <c>{ "set": true }</c> (05 §2).</summary>
public sealed record SettingsView(
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyDictionary<string, SecretState> Secrets,
    bool RestartRequired);

public sealed record SecretState(bool Set);
