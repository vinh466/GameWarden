using System.Collections.Concurrent;
using GameWarden.Contracts;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Adapters;
using GameWarden.Core.Events;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Integrations;

/// <summary>
/// Attaches to the console stream for adapters with a <c>console</c> integration (docs/17): parses player join, online
/// and offline lines into <see cref="ConsolePlayerRegistry"/>, and seeds a running instance from its own log when Core
/// starts or reattaches. It never sends anything to the game; the mapping lives in the adapter's <c>events</c>.
/// </summary>
internal sealed class ConsolePlayerMonitor(
    CoreEvents events,
    Supervisor supervisor,
    SupervisorContext context,
    AdapterCatalog adapters,
    ConsolePlayerRegistry players,
    ILogger<ConsolePlayerMonitor> logger) : IHostedService
{
    private const int SeedFiles = 14;

    private readonly ConcurrentDictionary<string, CachedMap> _maps = new(StringComparer.Ordinal);
    private bool _subscribed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            events.ConsoleLine += OnConsoleLine;
            events.InstanceChanged += OnInstanceChanged;
            events.InstanceRemoved += OnInstanceRemoved;
            _subscribed = true;
        }

        // Seed before the host reports started, so a tab opened immediately after a Core restart already has the roster.
        await Task.Run(SeedRunning, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            events.ConsoleLine -= OnConsoleLine;
            events.InstanceChanged -= OnInstanceChanged;
            events.InstanceRemoved -= OnInstanceRemoved;
            _subscribed = false;
        }

        return Task.CompletedTask;
    }

    private void OnConsoleLine(string instanceId, OutputLine line)
    {
        if (MapFor(instanceId) is not { } map)
        {
            return;
        }

        foreach (var match in map.Match(line.Text))
        {
            players.Apply(instanceId, match);
        }
    }

    private void OnInstanceChanged(InstanceSnapshot? before, InstanceSnapshot after)
    {
        if (after.State is ActualState.Stopped or ActualState.Crashed or ActualState.Faulted or ActualState.NotInstalled)
        {
            players.Clear(after.Id);
        }
    }

    private void OnInstanceRemoved(string instanceId) => players.Clear(instanceId);

    private void SeedRunning()
    {
        foreach (var snapshot in supervisor.List().Where(snapshot => snapshot.State is ActualState.Starting or ActualState.Running))
        {
            try
            {
                Seed(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning("Could not seed console players of {Instance}: {Reason}", LogText.Clean(snapshot.Id), ex.Message);
            }
        }
    }

    private void Seed(InstanceSnapshot snapshot)
    {
        if (MapFor(snapshot.Id) is not { } map)
        {
            return;
        }

        var paths = InstancePaths.For(context.Options.DataRoot, snapshot.Id);
        var files = context.Files.Run(() =>
            Directory.Exists(paths.Logs)
                ? Directory.GetFiles(paths.Logs, "*.log", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .Take(SeedFiles)
                    .ToArray()
                : []);
        var lines = new List<string>();
        var foundReset = false;
        foreach (var file in files)
        {
            var content = context.Files.Run(() => ReadShared(file));
            var reset = Array.FindLastIndex(content, line => map.Match(line).Any(match => match.Kind == ConsoleEventKind.Reset));
            if (reset >= 0)
            {
                lines.InsertRange(0, content.Skip(reset));
                foundReset = true;
                break;
            }

            lines.InsertRange(0, content);
        }

        players.Clear(snapshot.Id);
        if (!foundReset)
        {
            logger.LogDebug("No start marker in the last {Files} log file(s) of {Instance}; roster starts empty.", files.Length, LogText.Clean(snapshot.Id));
            return;
        }

        foreach (var match in lines.SelectMany(map.Match))
        {
            players.Apply(snapshot.Id, match);
        }

        logger.LogDebug("Seeded {Count} console line(s) for {Instance}.", lines.Count, LogText.Clean(snapshot.Id));
    }

    private ConsoleEventMap? MapFor(string instanceId)
    {
        var snapshot = supervisor.Find(instanceId)?.Snapshot;
        if (snapshot is null)
        {
            return null;
        }

        var adapter = adapters.Find(snapshot.AdapterId);
        if (adapter?.Integration is not { Transport: "console", Events: { Count: > 0 } mappings })
        {
            return null;
        }

        if (_maps.TryGetValue(adapter.Id, out var cached) && ReferenceEquals(cached.Adapter, adapter))
        {
            return cached.Map;
        }

        try
        {
            var map = ConsoleEventMap.Build(mappings);
            _maps[adapter.Id] = new CachedMap(adapter, map);
            return map;
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(
                "Console event map of adapter {Adapter} is invalid: {Reason}",
                LogText.Clean(adapter.Id),
                ex.Message);
            return null;
        }
    }

    private static string[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
    }

    private sealed record CachedMap(AdapterDefinition Adapter, ConsoleEventMap Map);
}
