using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using GameWarden.Contracts.Runner;

namespace GameWarden.Runner;

/// <summary>
/// Serves <c>\\.\pipe\gamewarden.runner.&lt;id&gt;</c> to one client (Core) at a time, for as long as the Runner lives.
/// Who may open it is the platform's rule (<see cref="IRunnerPlatform.CreatePipeServer"/>): on Windows only SYSTEM,
/// Administrators and the Runner's own user; on Linux, a Unix domain socket, only the current user (ADR-0016).
/// </summary>
internal sealed class PipeServer(GameHost host, IRunnerPlatform platform, DiagnosticLog diagnostics)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = platform.CreatePipeServer(RunnerPipe.NameFor(host.InstanceId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Write($"Cannot create the pipe: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            await using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    await new PipeSession(pipe, host).RunAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                // UnauthorizedAccessException: on Linux, .NET refuses a peer of another user only after accepting it
                // (CurrentUserOnly); the Runner keeps serving.
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
                {
                    diagnostics.Write($"Pipe session ended: {ex.Message}");
                }
            }
        }
    }
}

/// <summary>One connected client: handshake, then requests in order, with events pushed in between.</summary>
internal sealed class PipeSession(Stream stream, GameHost host)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var channel = new PipeChannel(stream);
        var first = await channel.ReceiveAsync(cancellationToken);
        if (first is not Hello hello)
        {
            if (first is PipeRequest request)
            {
                await channel.SendAsync(new Rejected("Send hello first.") { ReplyTo = request.Id }, cancellationToken);
            }

            return;
        }

        if (!hello.Protocols.Contains(RunnerPipe.ProtocolVersion))
        {
            await channel.SendAsync(new Rejected($"This Runner speaks protocol {RunnerPipe.ProtocolVersion}.") { ReplyTo = hello.Id }, cancellationToken);
            return;
        }

        var events = Channel.CreateBounded<PipeMessage>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        using var subscription = host.Subscribe(events.Writer);
        // A client that connects while the game is still being started would otherwise learn no game PID (Core keeps
        // the welcome's). Output from that moment waits in the subscription.
        await host.GameStarted.WaitAsync(cancellationToken);
        await channel.SendAsync(new Welcome(RunnerPipe.ProtocolVersion, RunnerInfo.Version, host.InstanceId, host.Status) { ReplyTo = hello.Id }, cancellationToken);

        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = PumpEventsAsync(channel, events.Reader, session.Token);
        try
        {
            while (await channel.ReceiveAsync(session.Token) is { } message)
            {
                var reply = message is PipeRequest request ? host.Handle(request) : new Rejected("Only requests are accepted.");
                await channel.SendAsync(reply, session.Token);
            }
        }
        finally
        {
            await session.CancelAsync();
            try
            {
                await pump;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                // The client is gone or the Runner is shutting down.
            }
        }
    }

    private static async Task PumpEventsAsync(PipeChannel channel, ChannelReader<PipeMessage> events, CancellationToken cancellationToken)
    {
        await foreach (var message in events.ReadAllAsync(cancellationToken))
        {
            await channel.SendAsync(message, cancellationToken);
        }
    }
}
