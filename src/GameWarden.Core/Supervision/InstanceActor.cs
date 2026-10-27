using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using GameWarden.Contracts;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Adapters;
using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Processes;

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

    public Task<CommandResult> StartAsync() => Ask(StartCoreAsync);

    public Task<CommandResult> StopAsync() => Ask(StopCoreAsync);

    public Task<CommandResult> RestartAsync() => Ask(RestartCoreAsync);

    public Task<CommandResult> SendConsoleAsync(string command) => Ask(() => SendConsoleCoreAsync(command));

    public Task<IReadOnlyList<OutputLine>> TailAsync(int lines) => Ask(() => TailCoreAsync(lines));

    /// <summary>Core is going away (service stop, update): let go of the Runner and keep the game running (ADR-0003).</summary>
    public Task DetachAsync() => Run(DetachCoreAsync);

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
                done.TrySetResult(await work());
            }
            catch (Exception ex)
            {
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
            }
        }
    }

    // ----- commands --------------------------------------------------------------------------

    private async Task<CommandResult> StartCoreAsync()
    {
        switch (_state)
        {
            case ActualState.Starting or ActualState.Running:
                return CommandResult.NothingToDo(BuildSnapshot(), "Already running.");
            case ActualState.Stopping:
                return CommandResult.Rejected(BuildSnapshot(), "The instance is stopping.");
            case ActualState.NotInstalled:
                return CommandResult.Rejected(BuildSnapshot(), "The game is not installed.");
        }

        _restarts.Reset(); // a manual start resets the crash counter (03 §4)
        _record.DesiredState = DesiredState.Running;
        await SaveAsync();
        return await LaunchAsync();
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

    private async Task<CommandResult> RestartCoreAsync()
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
                return await StartCoreAsync();
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
        var adapter = _context.Adapters.Find(_record.AdapterId);
        if (adapter is null)
        {
            return await LaunchFailedAsync($"Adapter '{_record.AdapterId}' is missing or invalid.");
        }

        var paths = Paths;
        try
        {
            paths.Create();
            var spec = LaunchSpecBuilder.Build(adapter, Id, _record.DisplayName, paths, Ports(), Settings());
            await File.WriteAllTextAsync(paths.LaunchSpecFile, JsonSerializer.Serialize(spec, ContractJson.Options));
            File.Delete(Path.Combine(paths.RunnerState, RunnerExitRecord.FileName));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
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
        if (_record.DesiredState != DesiredState.Running)
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
        if (_restartAfterStop)
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
                StartPolling(FileProbe(file, new Regex(readiness.Pattern!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))));
                return;

            default: // port
                StartPhase(TimeSpan.FromSeconds(readiness.TimeoutSec), OnReadinessTimeoutAsync);
                var port = Ports().GetValueOrDefault(readiness.Port ?? string.Empty);
                var protocol = adapter.Ports.FirstOrDefault(p => p.Name == readiness.Port)?.Protocol ?? "tcp";
                StartPolling(() => IsListening(protocol, port));
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NetworkInformationException)
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

    private static Func<bool> FileProbe(string path, Regex pattern)
    {
        long offset = File.Exists(path) ? new FileInfo(path).Length : 0;
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

    private static bool IsListening(string protocol, int port)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = protocol == "udp" ? properties.GetActiveUdpListeners() : properties.GetActiveTcpListeners();
        return listeners.Any(endpoint => endpoint.Port == port);
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
            return File.Exists(path) ? JsonSerializer.Deserialize<RunnerExitRecord>(File.ReadAllText(path), ContractJson.Options)?.ExitCode : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private IReadOnlyDictionary<string, int> Ports() =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(_record.PortsJson, ContractJson.Options) ?? new Dictionary<string, int>();

    private IReadOnlyDictionary<string, string> Settings() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(_record.SettingsJson, ContractJson.Options) ?? new Dictionary<string, string>();

    private InstanceSnapshot BuildSnapshot() => new(
        _record.Id,
        _record.DisplayName,
        _record.AdapterId,
        _record.DesiredState,
        _state,
        ActiveJobId: null,
        _record.RunnerPid,
        _gamePid,
        _restarts.RecentRestarts,
        _lastProblem);
}
