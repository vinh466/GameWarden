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
        _diagnostics.Write($"Game exited with code {exitCode}; waiting up to {_spec.LingerSeconds} s for Core to acknowledge.");
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
            SendCtrlC => SendCtrlCToGame(),
            WriteStdin stdin => WriteToGameStdin(stdin.Text),
            KillGame => KillGameTree(),
            AckExit => AcknowledgeExit(),
            Hello => new Rejected("Already connected."),
            _ => new Rejected($"Unknown request {request.GetType().Name}."),
        };
        return reply with { ReplyTo = request.Id };
    }

    public ValueTask DisposeAsync()
    {
        _output.Dispose();
        _shutdown.Dispose();
        _stopTail.Dispose();
        _game?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void StartGame()
    {
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
            RecordExit(-1, gamePid: null);
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
        // Let the redirected output drain, but do not wait forever on a grandchild holding the pipe open.
        using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await game.WaitForExitAsync(drain.Token);
        }
        catch (OperationCanceledException)
        {
            _diagnostics.Write("Game output did not drain within 5 s.");
        }

        if (_platform.OtherProcessCount() > 0)
        {
            _output.Add(OutputStream.Runner, "The main process exited; waiting for the processes it started.");
            while (_platform.OtherProcessCount() > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        // One more poll so the console log's last lines (e.g. "World saved") are relayed before the exit.
        _stopTail.CancelAfter(TimeSpan.FromMilliseconds(500));
        await _tail;
        RecordExit(game.ExitCode, game.Id);
    }

    private void RecordExit(int exitCode, int? gamePid)
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

        _output.Add(OutputStream.Runner, $"Game exited with code {exitCode}.");
        try
        {
            var record = new RunnerExitRecord(exitCode, at, gamePid);
            File.WriteAllText(Path.Combine(_spec.StateDirectory, RunnerExitRecord.FileName), JsonSerializer.Serialize(record, ContractJson.Options));
        }
        catch (IOException ex)
        {
            _diagnostics.Write($"Could not write the exit record: {ex.Message}");
        }

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

    private PipeReply SendCtrlCToGame()
    {
        if (RunningGame() is not { } game)
        {
            return new Rejected("The game is not running.");
        }

        if (!_platform.TrySendInterrupt(game, out var error))
        {
            return new Rejected(error);
        }

        _output.Add(OutputStream.Runner, $"Sent {_platform.InterruptName}.");
        return new Accepted();
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

    private PipeReply KillGameTree()
    {
        if (RunningGame() is not { } game)
        {
            return new Rejected("The game is not running.");
        }

        try
        {
            game.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // The main process may already be gone while its children still run.
            _diagnostics.Write($"Kill of the main process failed: {ex.Message}");
        }

        _platform.KillOtherProcesses();
        _output.Add(OutputStream.Runner, "Killed the game and every process it started.");
        return new Accepted();
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
