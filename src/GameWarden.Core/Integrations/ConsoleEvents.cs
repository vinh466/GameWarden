using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GameWarden.Core.Adapters;

namespace GameWarden.Core.Integrations;

internal enum ConsoleEventKind
{
    Joining,
    Online,
    Offline,
    Reset,
}

/// <summary>One matched console line; only the fields its event kind uses are set (docs/17 §3).</summary>
internal sealed record ConsoleEventMatch(
    ConsoleEventKind Kind,
    string? Id = null,
    string? Name = null,
    string? Zdo = null,
    string? Key = null);

/// <summary>
/// The adapter's console mappings, compiled once per adapter definition. A future Valheim log line is a new entry in
/// <c>adapter.json</c>, not a code change; an entry that no longer works can be removed the same way.
/// </summary>
internal sealed class ConsoleEventMap
{
    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private readonly IReadOnlyList<(ConsoleEventKind Kind, Regex Pattern)> _patterns;

    private ConsoleEventMap(IReadOnlyList<(ConsoleEventKind, Regex)> patterns) => _patterns = patterns;

    public static ConsoleEventMap Build(IReadOnlyList<AdapterConsoleEvent> events) => new(
        [.. events.Select(mapping => (Kind(mapping.Event), new Regex(mapping.Pattern, Options)))]);

    public IReadOnlyList<ConsoleEventMatch> Match(string line)
    {
        List<ConsoleEventMatch>? matches = null;
        foreach (var (kind, pattern) in _patterns)
        {
            var match = pattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            matches ??= [];
            matches.Add(new ConsoleEventMatch(
                kind,
                Value(match, "id"),
                Value(match, "name"),
                Value(match, "zdo"),
                Value(match, "key")));
        }

        return matches ?? [];
    }

    private static string? Value(Match match, string group) =>
        match.Groups[group] is { Success: true } value && !string.IsNullOrWhiteSpace(value.Value)
            ? value.Value.Trim()
            : null;

    private static ConsoleEventKind Kind(string value) => value switch
    {
        "joining" => ConsoleEventKind.Joining,
        "online" => ConsoleEventKind.Online,
        "offline" => ConsoleEventKind.Offline,
        "reset" => ConsoleEventKind.Reset,
        _ => throw new InvalidOperationException($"Unknown console event '{value}'."),
    };
}

/// <summary>The player seen in one instance's console; <see cref="Id"/> is the platform ID used by adminlist.txt.</summary>
internal sealed record ConsolePlayer(string? Id, string? Name, string? Zdo, bool Online);

/// <summary>
/// Correlates the console's connection lines with its character lines (docs/17 §3): a connection gives the platform
/// id, the following <c>Got character</c> line gives the name and ZDO, and a close line marks the player offline.
/// Correlation is per instance and lives only in memory; a Core restart is rebuilt from the instance log.
/// </summary>
internal sealed class ConsolePlayerRegistry
{
    private readonly ConcurrentDictionary<string, InstanceRoster> _rosters = new(StringComparer.Ordinal);

    public void Apply(string instanceId, ConsoleEventMatch match) => Roster(instanceId).Apply(match);

    public void Clear(string instanceId) => _rosters.TryRemove(instanceId, out _);

    public IReadOnlyList<ConsolePlayer> Snapshot(string instanceId) =>
        _rosters.TryGetValue(instanceId, out var roster) ? roster.Snapshot() : [];

    private InstanceRoster Roster(string instanceId) => _rosters.GetOrAdd(instanceId, _ => new InstanceRoster());

    private sealed class InstanceRoster
    {
        private readonly object _gate = new();
        private readonly List<string> _joining = [];
        private readonly Dictionary<string, Entry> _players = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _keyByZdo = new(StringComparer.Ordinal);

        public void Apply(ConsoleEventMatch match)
        {
            lock (_gate)
            {
                switch (match.Kind)
                {
                    case ConsoleEventKind.Joining when match.Id is { } id:
                        _joining.RemoveAll(pending => string.Equals(pending, id, StringComparison.Ordinal));
                        _joining.Add(id);
                        if (_joining.Count > 64)
                        {
                            _joining.RemoveAt(0);
                        }

                        break;
                    case ConsoleEventKind.Online when match.Name is { } name && match.Zdo is { } zdo:
                        Online(name, zdo);
                        break;
                    case ConsoleEventKind.Offline when match.Key is { } key:
                        Offline(key);
                        break;
                    case ConsoleEventKind.Reset:
                        Reset();
                        break;
                }
            }
        }

        public IReadOnlyList<ConsolePlayer> Snapshot()
        {
            lock (_gate)
            {
                return
                [
                    .. _players.Values
                        .Where(player => player.Online)
                        .OrderBy(player => player.Name ?? player.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(player => new ConsolePlayer(player.Id, player.Name, player.Zdo, player.Online)),
                ];
            }
        }

        private void Online(string name, string zdo)
        {
            Entry? player = null;
            if (_keyByZdo.TryGetValue(zdo, out var knownKey))
            {
                _players.TryGetValue(knownKey, out player);
            }

            if (player is null && _joining.Count > 0)
            {
                var id = _joining[0];
                _joining.RemoveAt(0);
                var key = Key(id);
                if (!_players.TryGetValue(key, out player))
                {
                    player = new Entry(id);
                    _players[key] = player;
                }
            }

            if (player is null)
            {
                // A character without a matching connection line: still show it, but it cannot be made admin.
                player = new Entry(null);
                _players[Key(null, zdo)] = player;
            }

            if (player.Zdo is { } oldZdo && !string.Equals(oldZdo, zdo, StringComparison.Ordinal))
            {
                _keyByZdo.Remove(oldZdo);
            }

            player.Name = name;
            player.Zdo = zdo;
            player.Online = true;
            _keyByZdo[zdo] = Key(player.Id, zdo);
        }

        private void Offline(string key)
        {
            _joining.RemoveAll(pending => string.Equals(pending, key, StringComparison.Ordinal));
            if (_players.TryGetValue(key, out var byId))
            {
                byId.Online = false;
                return;
            }

            if (_keyByZdo.TryGetValue(key, out var playerKey) && _players.TryGetValue(playerKey, out var byZdo))
            {
                byZdo.Online = false;
            }
        }

        private void Reset()
        {
            _joining.Clear();
            _players.Clear();
            _keyByZdo.Clear();
        }

        private static string Key(string? id, string? zdo = null) =>
            id is { Length: > 0 } ? id : $"zdo:{zdo}";

        private sealed class Entry(string? id)
        {
            public string? Id { get; } = id;

            public string? Name { get; set; }

            public string? Zdo { get; set; }

            public bool Online { get; set; }
        }
    }
}
