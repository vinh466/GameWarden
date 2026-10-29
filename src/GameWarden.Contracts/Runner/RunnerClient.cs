using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading.Channels;

namespace GameWarden.Contracts.Runner;

/// <summary>
/// Core's side of the Runner pipe: connects, checks that the pipe really belongs to the expected Runner
/// process (a mod running as the same user could create a fake pipe — OQ-26), negotiates the protocol
/// and exposes requests as methods and events as a channel.
/// </summary>
public sealed class RunnerClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly PipeChannel _channel;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<PipeReply>> _pending = new();
    private readonly Channel<PipeMessage> _events = Channel.CreateUnbounded<PipeMessage>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _closed = new();
    private Task _readLoop = Task.CompletedTask;
    private int _nextId;

    private RunnerClient(NamedPipeClientStream pipe, int serverProcessId)
    {
        _pipe = pipe;
        _channel = new PipeChannel(pipe);
        ServerProcessId = serverProcessId;
    }

    /// <summary>
    /// Process id of the pipe's server, as reported by the operating system: <c>GetNamedPipeServerProcessId</c> on
    /// Windows, <c>SO_PEERCRED</c> on Linux (ADR-0016).
    /// </summary>
    public int ServerProcessId { get; }

    public Welcome Welcome { get; private set; } = null!;

    /// <summary><see cref="OutputEvent"/> and <see cref="ExitedEvent"/> pushed by the Runner. Completes when the pipe closes.</summary>
    public ChannelReader<PipeMessage> Events => _events.Reader;

    public static async Task<RunnerClient> ConnectAsync(
        string instanceId,
        int? expectedRunnerPid,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", RunnerPipe.NameFor(instanceId), PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeout, cancellationToken);
            var serverPid = PipeServerProcess.Of(pipe);
            if (expectedRunnerPid is { } expected && serverPid != expected)
            {
                throw new RunnerIdentityException(instanceId, expected, serverPid);
            }

            var client = new RunnerClient(pipe, serverPid);
            await client.HandshakeAsync(cancellationToken);
            return client;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public async Task<RunnerStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        ((StatusReply)await RequestAsync(new GetStatus(), cancellationToken)).Status;

    public Task SendCtrlCAsync(CancellationToken cancellationToken = default) => CommandAsync(new SendCtrlC(), cancellationToken);

    public Task WriteStdinAsync(string text, CancellationToken cancellationToken = default) =>
        CommandAsync(new WriteStdin(text), cancellationToken);

    public Task KillAsync(CancellationToken cancellationToken = default) => CommandAsync(new KillGame(), cancellationToken);

    public Task AckExitAsync(CancellationToken cancellationToken = default) => CommandAsync(new AckExit(), cancellationToken);

    public async Task<IReadOnlyList<OutputLine>> GetTailAsync(int lines, CancellationToken cancellationToken = default) =>
        ((TailReply)await RequestAsync(new GetTail(lines), cancellationToken)).Lines;

    public async ValueTask DisposeAsync()
    {
        await _closed.CancelAsync();
        try
        {
            await _readLoop;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Cancelling (or a pipe the Runner already closed) ends the read loop with one of these.
        }

        await _channel.DisposeAsync();
        await _pipe.DisposeAsync();
        _closed.Dispose();
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        var hello = new Hello([RunnerPipe.ProtocolVersion, RunnerPipe.PreviousProtocolVersion]) { Id = NextId() };
        await _channel.SendAsync(hello, cancellationToken);
        var reply = await _channel.ReceiveAsync(cancellationToken);
        Welcome = reply switch
        {
            Welcome welcome when welcome.ReplyTo == hello.Id => welcome,
            Rejected rejected => throw new RunnerCommandRejectedException(rejected.Reason),
            null => throw new IOException("The Runner closed the pipe during the handshake."),
            _ => throw new InvalidDataException($"Unexpected handshake reply: {reply.GetType().Name}."),
        };
        _readLoop = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _channel.ReceiveAsync(_closed.Token) is { } message)
            {
                if (message is PipeReply reply)
                {
                    if (_pending.TryRemove(reply.ReplyTo, out var waiter))
                    {
                        waiter.TrySetResult(reply);
                    }
                }
                else
                {
                    await _events.Writer.WriteAsync(message, _closed.Token);
                }
            }
        }
        finally
        {
            _events.Writer.TryComplete();
            foreach (var waiter in _pending.Values)
            {
                waiter.TrySetException(new IOException("The Runner pipe closed."));
            }
        }
    }

    private async Task CommandAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        var reply = await RequestAsync(request, cancellationToken);
        if (reply is not Accepted)
        {
            throw new InvalidDataException($"Unexpected reply {reply.GetType().Name} to {request.GetType().Name}.");
        }
    }

    private async Task<PipeReply> RequestAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        request = request with { Id = NextId() };
        var waiter = new TaskCompletionSource<PipeReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = waiter;
        await _channel.SendAsync(request, cancellationToken);
        var reply = await waiter.Task.WaitAsync(cancellationToken);
        return reply is Rejected rejected ? throw new RunnerCommandRejectedException(rejected.Reason) : reply;
    }

    private int NextId() => Interlocked.Increment(ref _nextId);
}

public sealed class RunnerIdentityException(string instanceId, int expectedPid, int actualPid)
    : IOException($"The Runner pipe of '{instanceId}' belongs to process {actualPid}, not to the Runner {expectedPid}.")
{
    public int ExpectedPid { get; } = expectedPid;

    public int ActualPid { get; } = actualPid;
}

public sealed class RunnerCommandRejectedException(string reason) : InvalidOperationException(reason);
