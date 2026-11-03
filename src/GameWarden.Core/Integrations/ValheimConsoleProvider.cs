using System.Text.RegularExpressions;
using GameWarden.Core.Adapters;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Integrations;

/// <summary>
/// Valheim without mods (docs/17): players come from the console event map, and admins from <c>adminlist.txt</c> in the
/// instance's <c>data</c> folder (the adapter passes it as <c>-savedir</c>). It never sends a console command and opens
/// no port; a change to the admin list takes effect at the next start, as Valheim itself documents.
/// </summary>
internal sealed partial class ValheimConsoleProvider(
    SupervisorContext hostContext,
    ConsolePlayerRegistry players) : IGameIntegrationProvider
{
    public const string ProviderId = "valheim-console";

    private readonly SemaphoreSlim _write = new(1, 1);

    public string Id => ProviderId;

    public IReadOnlyList<string> Capabilities => ["overview", "players", "admin"];

    public Task<GameIntegrationData> ReadAsync(GameIntegrationContext context, CancellationToken cancellationToken)
    {
        var paths = InstancePaths.For(hostContext.Options.DataRoot, context.InstanceId);
        var admins = ReadAdmins(paths);
        var online = players.Snapshot(context.InstanceId);
        var settings = context.Settings ?? new Dictionary<string, string>();
        var onlineIds = online.Where(player => player.Id is not null).Select(player => player.Id!).ToHashSet(StringComparer.Ordinal);

        var view = online
            .Select(player => Player(player, online: true, admin: player.Id is not null && admins.Contains(player.Id)))
            .Concat(admins
                .Where(id => !onlineIds.Contains(id))
                .Select(id => Player(new ConsolePlayer(id, null, null, false), online: false, admin: true)))
            .OrderByDescending(player => player.Online)
            .ThenBy(player => player.Name ?? player.UserId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var info = new GameIntegrationInfo(
            Version: null,
            ServerName: settings.GetValueOrDefault("serverName"),
            Description: null,
            WorldGuid: null,
            WorldName: settings.GetValueOrDefault("world"));
        return Task.FromResult(new GameIntegrationData(
            info,
            new GameIntegrationMetrics(null, view.Count(player => player.Online == true), null, null, null, null, null),
            view));
    }

    public async Task<GameIntegrationActionResult> ActAsync(
        GameIntegrationContext context,
        string action,
        GameIntegrationActionRequest request,
        CancellationToken cancellationToken)
    {
        if (action is not ("admin" or "unadmin"))
        {
            throw new GameIntegrationException(
                StatusCodes.Status400BadRequest,
                "unsupported_action",
                $"The Valheim integration does not support '{action}'.");
        }

        var id = request.UserId?.Trim();
        if (string.IsNullOrEmpty(id) || !AdminId().IsMatch(id))
        {
            throw new GameIntegrationException(
                StatusCodes.Status400BadRequest,
                "validation_failed",
                "userId must be a Valheim platform ID: 1-64 letters, digits or underscores.");
        }

        await _write.WaitAsync(cancellationToken);
        try
        {
            var paths = InstancePaths.For(hostContext.Options.DataRoot, context.InstanceId);
            ChangeAdmin(paths, id, add: action == "admin");
        }
        finally
        {
            _write.Release();
        }

        var verb = action == "admin" ? "Added to" : "Removed from";
        return new GameIntegrationActionResult(action, true, $"{verb} adminlist.txt; restart the server to apply it.");
    }

    private HashSet<string> ReadAdmins(InstancePaths paths)
    {
        var file = AdminFile(paths);
        try
        {
            return hostContext.Files.Run(() => ReadIds(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameIntegrationException(
                StatusCodes.Status500InternalServerError,
                "integration_file_failed",
                $"Could not read adminlist.txt: {ex.Message}");
        }
    }

    private void ChangeAdmin(InstancePaths paths, string id, bool add)
    {
        var file = AdminFile(paths);
        try
        {
            hostContext.Files.Run(() =>
            {
                var lines = File.Exists(file) ? [.. File.ReadAllLines(file)] : new List<string>();
                var existing = lines
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
                    .ToHashSet(StringComparer.Ordinal);
                if (add == existing.Contains(id))
                {
                    return;
                }

                if (add)
                {
                    lines.Add(id);
                }
                else
                {
                    lines.RemoveAll(line => string.Equals(line.Trim(), id, StringComparison.Ordinal));
                }

                var temporary = file + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
                File.WriteAllLines(temporary, lines);
                File.Move(temporary, file, overwrite: true);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameIntegrationException(
                StatusCodes.Status500InternalServerError,
                "integration_file_failed",
                $"Could not write adminlist.txt: {ex.Message}");
        }
    }

    private static HashSet<string> ReadIds(string file)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(file))
        {
            return ids;
        }

        foreach (var line in File.ReadAllLines(file))
        {
            var id = line.Trim();
            if (id.Length > 0 && !id.StartsWith("//", StringComparison.Ordinal))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string AdminFile(InstancePaths paths) => Path.Combine(paths.Data, "adminlist.txt");

    private static GameIntegrationPlayer Player(ConsolePlayer player, bool online, bool admin) => new(
        Name: player.Name,
        AccountName: null,
        PlayerId: player.Zdo,
        UserId: player.Id,
        Ip: null,
        Ping: null,
        X: null,
        Y: null,
        Level: null,
        BuildingCount: null,
        Online: online,
        IsAdmin: admin);

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AdminId();
}
