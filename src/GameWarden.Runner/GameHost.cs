using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GameWarden.Contracts;
using GameWarden.Contracts.Runner;

namespace GameWarden.Runner;

/// <summary>Starts the game on the Runner's console, relays its output and carries out Core's requests.</summary>
internal sealed class GameHost : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LaunchSpec _spec;
    private readonly IRunnerPlatform _platform;
    private readonly DiagnosticLog _diagnostics;
    private readonly OutputHub _output;
    private readonly object _gate = new();
    private readonly List<ChannelWriter<PipeMessage>> _subscribers = [];
    private readonly TaskCompletionSource _gameStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _gameExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exitAcknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationTokenSource _stopTail = new();
    private Task _tail = Task.CompletedTask;
    private Process? _game;
    private int? _gamePid;
    private DateTimeOffset? _startedAt;
    private int? _exitCode;
    private DateTimeOffset? _exitedAt;
    private string? _lastOperationId;
    private CancellationTokenSource? _stopHeartbeat;

    public GameHost(LaunchSpec spec, IRunnerPlatform platform, DiagnosticLog diagnostics)
    {
        _spec = spec;
        _platform = platform;
        _diagnostics = diagnostics;
        _output = new OutputHub(spec.LogDirectory, Math.Max(1, spec.RingBufferLines));
        _output.LineAdded += line => Publish(new OutputEvent(line));
    }

    public string InstanceId => _spec.InstanceId;

    public RunnerStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new RunnerStatus(
                    Environment.ProcessId,
                    _gamePid,
                    _startedAt,
                    GameRunning: _gamePid is not null && _exitCode is null,
                    _exitCode,
                    _exitedAt);
            }
        }
    }

    /// <summary>
    /// Completes once the game has started or failed to start. The pipe is served from the Runner's first moment, so a
    /// session waits for this before its welcome, which carries the game's PID to Core.
    /// </summary>
    public Task GameStarted => _gameStarted.Task;

    public async Task<int> RunAsync()
    {
        var serving = new PipeServer(this, _platform, _diagnostics).RunAsync(_shutdown.Token);
        try
        {
            StartGame();
        }
        finally
        {
            _gameStarted.TrySetResult();
        }

        var exitCode = await _gameExited.Task;
        _diagnostics.Write($"[{_lastOperationId ?? "run"}] Game exited with code {exitCode}; waiting up to {_spec.LingerSeconds} s for Core to acknowledge.");
        await Task.WhenAny(_exitAcknowledged.Task, Task.Delay(TimeSpan.FromSeconds(_spec.LingerSeconds)));

        await _shutdown.CancelAsync();
        await serving;
        _diagnostics.Write("Runner exiting.");
        return 0;
    }

    public IDisposable Subscribe(ChannelWriter<PipeMessage> subscriber)
    {
        lock (_gate)
        {
            _subscribers.Add(subscriber);
        }

        return new Subscription(this, subscriber);
    }

    public PipeReply Handle(PipeRequest request)
    {
        PipeReply reply = request switch
        {
            GetStatus => new StatusReply(Status),
            GetTail tail => new TailReply(_output.Tail(Math.Clamp(tail.Lines, 0, _spec.RingBufferLines))),
            SendCtrlC ctrlC => SendCtrlCToGame(ctrlC.OperationId),
            WriteStdin stdin => WriteToGameStdin(stdin.Text),
            KillGame kill => KillGameTree(kill.OperationId),
            AckExit => AcknowledgeExit(),
            Hello => new Rejected("Already connected."),
            _ => new Rejected($"Unknown request {request.GetType().Name}."),
        };
        return reply with { ReplyTo = request.Id };
    }

    public ValueTask DisposeAsync()
    {
        CancelStopHeartbeat();
        _output.Dispose();
        _shutdown.Dispose();
        _stopTail.Dispose();
        _game?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void StartGame()
    {
        CancelStopHeartbeat();
        _lastOperationId = null;
        var info = new ProcessStartInfo(_spec.Executable)
        {
            WorkingDirectory = _spec.WorkingDirectory,
            UseShellExecute = false,
            // Stay on the Runner's console: CTRL+C only reaches processes that share it.
            CreateNoWindow = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var argument in _spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in _spec.Environment)
        {
            info.Environment[name] = value;
        }

        var game = new Process { StartInfo = info, EnableRaisingEvents = true };
        game.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _output.Add(OutputStream.Stdout, e.Data);
            }
        };
        game.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _output.Add(OutputStream.Stderr, e.Data);
            }
        };
        game.Exited += (_, _) => _ = Task.Run(() => OnGameExitedAsync(game));

        if (_spec.ConsoleLogFile is { } logFile)
        {
            LogFileTail.DeleteOld(logFile, _diagnostics);
        }

        try
        {
            game.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _output.Add(OutputStream.Runner, $"Could not start {_spec.Executable}: {ex.Message}");
            game.Dispose();
            RecordExit(-1, gamePid: null, operationId: null);
            return;
        }

        lock (_gate)
        {
            _game = game;
            _gamePid = game.Id;
            _startedAt = DateTimeOffset.UtcNow;
        }

        game.BeginOutputReadLine();
        game.BeginErrorReadLine();
        if (_spec.ConsoleLogFile is { } consoleLog)
        {
            var tail = new LogFileTail(consoleLog, line => _output.Add(OutputStream.Stdout, line));
            _tail = Task.Run(() => tail.RunAsync(_stopTail.Token));
        }

        _output.Add(OutputStream.Runner, $"Started {Path.GetFileName(_spec.Executable)} (pid {game.Id}).");
    }

    private async Task OnGameExitedAsync(Process game)
    {
        var operationId = _lastOperationId ?? "run";
        try
        {
            // Let the redirected output drain, but do not wait forever on a grandchild holding the pipe open.
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await game.WaitForExitAsync(drain.Token);
            }
            catch (OperationCanceledException)
            {
                _diagnostics.Write($"[{operationId}] game output did not drain within 5 s.");
            }

            if (_platform.OtherProcessCount() > 0)
            {
                _output.Add(OutputStream.Runner, $"[{operationId}] The main process exited; waiting for the processes it started.");
                var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
                while (_platform.OtherProcessCount() > 0 && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                if (_platform.OtherProcessCount() > 0)
                {
                    _diagnostics.Write($"[{operationId}] {_platform.OtherProcessCount()} process(es) started by the game are still alive after 10 s; recording the exit anyway.");
                }
            }

            // One more poll so the console log's last lines (e.g. "World saved") are relayed before the exit.
            _stopTail.CancelAfter(TimeSpan.FromMilliseconds(500));
            await Task.WhenAny(_tail, Task.Delay(TimeSpan.FromSeconds(2)));
            if (!_tail.IsCompleted)
            {
                _diagnostics.Write($"[{operationId}] the log tail did not stop within 2 s; recording the exit anyway.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            _diagnostics.Write($"[{operationId}] exit handler failed: {ex.Message}");
        }
        finally
        {
            // The exit is recorded even when the tail or a descendant misbehaves: Core must never wait forever.
            int exitCode;
            try
            {
                exitCode = game.ExitCode;
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }

            RecordExit(exitCode, game.Id, operationId);
        }
    }

    private void RecordExit(int exitCode, int? gamePid, string? operationId)
    {
        var at = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_exitCode is not null)
            {
                return;
            }

            _exitCode = exitCode;
            _exitedAt = at;
        }

        CancelStopHeartbeat();
        var op = operationId ?? _lastOperationId ?? "run";
        _output.Add(OutputStream.Runner, $"[{op}] Game exited with code {exitCode}.");
        try
        {
            var record = new RunnerExitRecord(exitCode, at, gamePid);
            File.WriteAllText(Path.Combine(_spec.StateDirectory, RunnerExitRecord.FileName), JsonSerializer.Serialize(record, ContractJson.Options));
        }
        catch (IOException ex)
        {
            _diagnostics.Write($"[{op}] could not write the exit record: {ex.Message}");
        }

        _diagnostics.Write($"[{op}] exit recorded: code={exitCode} gamePid={gamePid} at={at:O}.");
        Publish(new ExitedEvent(exitCode, at));
        _gameExited.TrySetResult(exitCode);
    }

    private Process? RunningGame()
    {
        lock (_gate)
        {
            return _exitCode is null ? _game : null;
        }
    }

    private PipeReply SendCtrlCToGame(string? operationId)
    {
        if (RunningGame() is not { } game)
        {
            return new Rejected("The game is not running.");
        }

        if (!_platform.TrySendInterrupt(game, out var error))
        {
            return new Rejected(error);
        }

        _lastOperationId = operationId ?? _lastOperationId;
        var op = _lastOperationId ?? "run";
        _output.Add(OutputStream.Runner, $"[{op}] Sent {_platform.InterruptName}.");
        _diagnostics.Write($"[{op}] ctrl-c sent to gamePid={game.Id}; waiting for the exit.");
        StartStopHeartbeat(op);
        return new Accepted();
    }

    /// <summary>
    /// While a stop is in flight, a line every 5 s says whether the game is dead and how many processes the job still
    /// holds: when a stop hangs, the Runner log shows exactly where (ADR-0034).
    /// </summary>
    private void StartStopHeartbeat(string operationId)
    {
        CancelStopHeartbeat();
        var cts = new CancellationTokenSource();
        _stopHeartbeat = cts;
        var started = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                    var status = Status;
                    var last = _output.Tail(1).LastOrDefault();
                    var lastText = last is { } line ? $"{line.At.ToLocalTime():HH:mm:ss}" : "none";
                    _diagnostics.Write(
                        $"[{operationId}] stop heartbeat elapsed={(DateTimeOffset.UtcNow - started).TotalSeconds:0.0}s gamePid={status.GamePid} gameRunning={status.GameRunning} jobProcesses={_platform.OtherProcessCount()} lastConsole={lastText}");
                }
            }
            catch (OperationCanceledException)
            {
                // The exit was recorded or the Runner is shutting down.
            }
            catch (Exception ex)
            {
                _diagnostics.Write($"[{operationId}] stop heartbeat failed: {ex.Message}");
            }
        });
    }

    private void CancelStopHeartbeat()
    {
        var heartbeat = _stopHeartbeat;
        _stopHeartbeat = null;
        heartbeat?.Cancel();
        heartbeat?.Dispose();
    }

    private PipeReply WriteToGameStdin(string text)
    {
        if (RunningGame() is not { } game)
        {
            return new Rejected("The game is not running.");
        }

        try
        {
            game.StandardInput.WriteLine(text);
            game.StandardInput.Flush();
            return new Accepted();
        }
        catch (IOException ex)
        {
            return new Rejected($"Could not write to the game's stdin: {ex.Message}");
        }
    }

    private PipeReply KillGameTree(string? operationId)
    {
        if (RunningGame() is not { } game)
        {
            return new Rejected("The game is not running.");
        }

        _lastOperationId = operationId ?? _lastOperationId;
        var op = _lastOperationId ?? "run";
        _diagnostics.Write($"[{op}] kill requested gamePid={game.Id} jobProcesses={_platform.OtherProcessCount()}.");
        string? failure = null;
        try
        {
            game.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException)
        {
            // The main process may already be gone, or one descendant could not be terminated.
            failure = ex.Message;
            _diagnostics.Write($"[{op}] kill of the game tree failed: {ex}");
        }

        _platform.KillOtherProcesses();
        var exited = false;
        try
        {
            exited = game.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _diagnostics.Write($"[{op}] waiting for the killed game failed: {ex.Message}");
        }

        bool hasExited;
        try
        {
            hasExited = game.HasExited;
        }
        catch (InvalidOperationException)
        {
            hasExited = true;
        }

        _diagnostics.Write(
            $"[{op}] kill result hasExited={hasExited} waitReturned={exited} failure={failure ?? "none"} jobProcesses={_platform.OtherProcessCount()}.");
        if (hasExited && Status.ExitCode is null)
        {
            // The process is gone but its Exited event was missed: record it here so Core cannot wait forever.
            int code;
            try
            {
                code = game.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = -1;
            }

            RecordExit(code, game.Id, op);
        }

        _output.Add(OutputStream.Runner, hasExited
            ? $"[{op}] Killed the game and every process it started."
            : $"[{op}] The game is still running after the kill.");
        return hasExited ? new Accepted() : new Rejected("The game did not exit within 5 s after the kill.");
    }

    /// <summary>
    /// Core has been told its acknowledgement was accepted (<see cref="PipeSession"/> calls this after the reply is
    /// written): only now may the Runner close the pipe and quit, or Core would see the pipe close instead of the reply.
    /// </summary>
    public void ExitAcknowledgementDelivered() => _exitAcknowledged.TrySetResult();

    private PipeReply AcknowledgeExit() =>
        Status.ExitCode is null ? new Rejected("The game is still running.") : new Accepted();

    private void Publish(PipeMessage message)
    {
        ChannelWriter<PipeMessage>[] targets;
        lock (_gate)
        {
            targets = [.. _subscribers];
        }

        foreach (var target in targets)
        {
            target.TryWrite(message);
        }
    }

    private sealed class Subscription(GameHost host, ChannelWriter<PipeMessage> subscriber) : IDisposable
    {
        public void Dispose()
        {
            lock (host._gate)
            {
                host._subscribers.Remove(subscriber);
            }
        }
    }
}
