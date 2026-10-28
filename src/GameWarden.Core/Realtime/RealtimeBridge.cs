using System.Threading.Channels;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Api;
using GameWarden.Core.Events;
using GameWarden.Core.Hosting;
using Microsoft.AspNetCore.SignalR;

namespace GameWarden.Core.Realtime;

/// <summary>
/// Event bus → SignalR (docs/05-api.md §3). Messages carry the API's own types (<see cref="Supervision.InstanceSnapshot"/>,
/// <see cref="JobInfo"/>, <see cref="ActivityItem"/>, <see cref="OutputLine"/>) or plain values, so the panel's
/// generated types describe them. One loop sends them in order; console lines go out in batches every 100 ms.
/// Clients that reconnect read the current state with REST (events are not replayed).
/// </summary>
public sealed class RealtimeBridge : BackgroundService
{
    public static readonly TimeSpan ConsoleBatch = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private const int MaxLinesPerBatch = 500;

    private readonly IHubContext<EventsHub> _hub;
    private readonly ILogger<RealtimeBridge> _logger;
    private readonly Channel<Func<IHubClients, Task>> _outbox = Channel.CreateUnbounded<Func<IHubClients, Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<string, List<OutputLine>> _console = new(StringComparer.Ordinal);

    public RealtimeBridge(CoreEvents events, IHubContext<EventsHub> hub, ILogger<RealtimeBridge> logger)
    {
        _hub = hub;
        _logger = logger;
        events.InstanceChanged += (_, after) => Send(clients => clients.All.SendAsync("InstanceChanged", after));
        events.InstanceRemoved += id => Send(clients => clients.All.SendAsync("InstanceRemoved", id));
        events.JobChanged += job => Send(clients => clients.All.SendAsync("JobUpdated", JobInfo.From(job)));
        events.ActivityAdded += item => Send(clients => clients.All.SendAsync("ActivityAdded", ActivityItem.From(item)));
        events.ConsoleLine += (id, line) =>
        {
            lock (_console)
            {
                if (!_console.TryGetValue(id, out var lines))
                {
                    _console[id] = lines = [];
                }

                lines.Add(line);
            }
        };
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(SendAsync(stoppingToken), FlushConsoleAsync(stoppingToken), HeartbeatAsync(stoppingToken));

    private void Send(Func<IHubClients, Task> message) => _outbox.Writer.TryWrite(message);

    private async Task SendAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _outbox.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await message(_hub.Clients);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "A realtime message could not be sent.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FlushConsoleAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ConsoleBatch);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                List<(string Id, List<OutputLine> Lines)> batches;
                lock (_console)
                {
                    batches = [.. _console.Where(pair => pair.Value.Count > 0).Select(pair => (pair.Key, pair.Value))];
                    _console.Clear();
                }

                foreach (var (id, lines) in batches)
                {
                    // A flood of output keeps its newest lines; the console tab can read the rest with REST.
                    var send = lines.Count > MaxLinesPerBatch ? lines[^MaxLinesPerBatch..] : lines;
                    Send(clients => clients.Group(EventsHub.ConsoleGroup(id)).SendAsync("ConsoleLines", id, send));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HeartbeatAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Heartbeat);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var uptime = (long)(DateTimeOffset.UtcNow - CoreInfo.StartedAt).TotalSeconds;
                Send(clients => clients.All.SendAsync("CoreStatus", CoreInfo.Version, uptime));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
