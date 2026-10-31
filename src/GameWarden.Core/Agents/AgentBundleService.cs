using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Jobs;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Agents;

/// <summary>
/// A redacted snapshot for deep diagnosis: system, processes, network, storage, Event Log, core/instance/job logs
/// and instance config text. It contains no secrets, database, keys, tokens or save files, and it expires.
/// </summary>
public sealed class AgentBundleService(
    IOptions<GameWardenOptions> options,
    AgentDiagnosticsService diagnostics,
    AgentLogService logs,
    AgentRedactor redactor,
    Supervisor supervisor,
    JobService jobs,
    TimeProvider time,
    ILogger<AgentBundleService> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private const long MaxBytes = 25L * 1024 * 1024;
    private const int MaxEntries = 500;

    public async Task<AgentBundleView> CreateAsync(
        CreateAgentBundleRequest request,
        AgentPrincipal principal,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var id = Guid.NewGuid().ToString("N")[..12];
        var folder = Path.Combine(options.Value.TempPath, "agent-bundles");
        Directory.CreateDirectory(folder);
        Prune(folder, now);
        var path = Path.Combine(folder, id + ".zip");

        var requested = request.InstanceIds?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var instances = requested.Length > 0
            ? requested.Select(supervisor.Find).OfType<InstanceActor>().ToArray()
            : [.. supervisor.List().Select(snapshot => supervisor.Find(snapshot.Id)).OfType<InstanceActor>()];
        if (requested.Length > 0 && instances.Length != requested.Length)
        {
            throw new AgentRequestException(StatusCodes.Status404NotFound, "not_found", "One of the requested instances does not exist.");
        }

        var sinceHours = Math.Clamp(request.SinceHours ?? 24, 1, 168);
        var contents = new List<string>();
        var files = new List<(string Name, byte[] Bytes)>();
        void Add(string name, string? text)
        {
            if (text is null)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(redactor.Redact(text));
            if (files.Count >= MaxEntries || files.Sum(file => file.Bytes.Length) + bytes.Length > MaxBytes)
            {
                return;
            }

            files.Add((name, bytes));
            contents.Add(name);
        }

        Add("manifest.json", JsonSerializer.Serialize(new
        {
            createdAt = now,
            coreVersion = CoreInfo.Version,
            agent = new { principal.ClientId, principal.ClientName, principal.TokenId },
            instances = instances.Select(instance => instance.Id),
            sinceHours,
            redacted = true,
            note = "Secrets, keys, the database, save files and token files are not included.",
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        Add("system.json", JsonSerializer.Serialize(diagnostics.SystemInfo(), JsonOptions()));
        Add("processes.json", JsonSerializer.Serialize(diagnostics.Processes(300), JsonOptions()));
        Add("network.json", JsonSerializer.Serialize(diagnostics.Network(), JsonOptions()));
        Add("storage.json", JsonSerializer.Serialize(diagnostics.Storage(), JsonOptions()));

        var application = diagnostics.Events("Application", "warning", sinceHours, 100);
        var system = diagnostics.Events("System", "warning", sinceHours, 100);
        Add("events/application.txt", FormatEvents(application));
        Add("events/system.txt", FormatEvents(system));

        try
        {
            var core = logs.Tail("core", null, 500, null, null);
            Add("logs/core.log", string.Join('\n', core.Lines.Select(line => line.Text)));
        }
        catch (AgentRequestException)
        {
            // No core log yet.
        }

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var console = logs.Tail("instance", instance.Id, 500, null, null);
                Add($"instances/{instance.Id}/console.log", string.Join('\n', console.Lines.Select(line => line.Text)));
            }
            catch (AgentRequestException)
            {
                // No console log yet.
            }

            AddInstanceConfig(instance, Add);
        }

        var selectedIds = instances.Select(instance => instance.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var job in (await jobs.ListAsync(null, cancellationToken))
                     .Where(job => job.InstanceId is null || selectedIds.Contains(job.InstanceId))
                     .OrderByDescending(job => job.CreatedAt)
                     .Take(10))
        {
            var text = string.Join('\n', JobLog.Tail(options.Value.JobLogsPath, job.Id, 200).Select(redactor.Redact));
            Add($"jobs/{job.Id}-{job.Type}.log", text);
        }

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, bytes) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                await using var stream = entry.Open();
                await stream.WriteAsync(bytes, cancellationToken);
            }
        }

        var info = new FileInfo(path);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));
        logger.LogInformation("Agent {Client} created diagnostics bundle {Id} ({Bytes} bytes).", principal.ClientId, id, info.Length);
        return new AgentBundleView(id, "ready", info.Length, hash, now, now.Add(Lifetime), contents);
    }

    public AgentBundleView? Get(string id)
    {
        if (!IsId(id))
        {
            return null;
        }

        var path = Path.Combine(options.Value.TempPath, "agent-bundles", id + ".zip");
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.CreationTimeUtc < time.GetUtcNow().UtcDateTime.Add(-Lifetime))
        {
            File.Delete(path);
            return null;
        }

        return new AgentBundleView(
            id,
            "ready",
            info.Length,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            new DateTimeOffset(info.CreationTimeUtc),
            new DateTimeOffset(info.CreationTimeUtc).Add(Lifetime),
            []);
    }

    public string? PathOf(string id) => IsId(id)
        ? Path.Combine(options.Value.TempPath, "agent-bundles", id + ".zip")
        : null;

    public static bool IsId(string? id) =>
        id is { Length: 12 } && id.All(character => char.IsAsciiHexDigit(character));

    private void AddInstanceConfig(InstanceActor instance, Action<string, string?> add)
    {
        var paths = InstancePaths.For(options.Value.DataRoot, instance.Id);
        try
        {
            var instanceFile = Path.Combine(paths.Root, "instance.json");
            if (File.Exists(instanceFile) && new FileInfo(instanceFile).Length <= 512 * 1024)
            {
                add($"instances/{instance.Id}/instance.json", File.ReadAllText(instanceFile));
            }

            if (!Directory.Exists(paths.Config))
            {
                return;
            }

            foreach (var file in new DirectoryInfo(paths.Config).EnumerateFiles("*", SearchOption.AllDirectories)
                         .Where(file => IsText(file.Extension) && file.Length <= 512 * 1024)
                         .Take(100))
            {
                var relative = Path.GetRelativePath(paths.Config, file.FullName).Replace('\\', '/');
                add($"instances/{instance.Id}/config/{relative}", File.ReadAllText(file.FullName));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsText(string extension) =>
        extension.ToLowerInvariant() is ".cfg" or ".ini" or ".json" or ".yml" or ".yaml" or ".txt" or ".xml" or ".properties" or ".log";

    private static string FormatEvents(AgentEventsView events) =>
        events.Supported
            ? string.Join('\n', events.Entries.Select(entry => $"{entry.At:O} {entry.Level} {entry.Source} {entry.Id}: {entry.Message}"))
            : $"Event Log not available: {events.Reason}";

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private void Prune(string folder, DateTimeOffset now)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.zip"))
            {
                if (now - new DateTimeOffset(File.GetCreationTimeUtc(file)) > Lifetime.Add(TimeSpan.FromHours(23)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
