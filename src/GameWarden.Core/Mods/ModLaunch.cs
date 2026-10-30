using System.Security.Cryptography;
using GameWarden.Core.Adapters;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Mods;

/// <summary>What mods add to a start: the loader's arguments, or why the start cannot happen.</summary>
public sealed record ModLaunchPlan(IReadOnlyList<string> Arguments, string? Problem = null)
{
    public static ModLaunchPlan None { get; } = new([]);
}

/// <summary>
/// The mods part of every start (docs/13 §4.3): a user start, a crash restart and a start after a job all go through
/// <see cref="Supervision.InstanceActor"/>'s launch path, which asks this. Separate from <see cref="ModsService"/>,
/// which depends on the supervisor. Instance files are touched only through <see cref="IInstanceFiles"/>, synchronously
/// (ADR-0009 §3).
/// <list type="bullet">
/// <item>An adapter without mods: nothing changes.</item>
/// <item><i>Mods off</i>: the loader's off arguments once its entry files are in <c>server\</c>; a server that never had
/// mods gets no extra arguments.</item>
/// <item><i>Mods on</i> without a ready active set: the start is refused.</item>
/// <item><i>Mods on</i>: the loader's entry files are copied into <c>server\</c> when they differ by SHA-256; at the
/// set's first start the configs of the set that ran before are copied over its defaults (§4.7); the set becomes the
/// running set; the loader's on arguments point the game at it.</item>
/// </list>
/// </summary>
public sealed class ModLaunch(
    IInstanceFiles files,
    IDbContextFactory<GameWardenDb> database,
    IEnumerable<IModLoader> loaders,
    TimeProvider time,
    ILogger<ModLaunch> logger)
{
    /// <summary>A user start or restart asks first, so a refusal changes nothing; null when mods do not stop it.</summary>
    public async Task<string?> CheckAsync(string instanceId, AdapterDefinition adapter)
    {
        if (adapter.Mods is not { } mods)
        {
            return null;
        }

        await using var db = await database.CreateDbContextAsync();
        var row = await db.InstanceMods.AsNoTracking().FirstOrDefaultAsync(row => row.InstanceId == instanceId);
        if (row is not { Enabled: true })
        {
            return null;
        }

        return Loader(mods) is null ? NoLoader(mods) : await ReadySetAsync(db, row) is null ? NoSet(instanceId) : null;
    }

    /// <summary>Before each launch: the arguments to add after the adapter's own, or the problem that stops the start.</summary>
    public async Task<ModLaunchPlan> PrepareAsync(string instanceId, AdapterDefinition adapter, InstancePaths paths)
    {
        if (adapter.Mods is not { } mods)
        {
            return ModLaunchPlan.None;
        }

        try
        {
            await using var db = await database.CreateDbContextAsync();
            var row = await db.InstanceMods.FirstOrDefaultAsync(row => row.InstanceId == instanceId);
            var loader = Loader(mods);
            if (row is not { Enabled: true })
            {
                return loader is not null && files.Run(() => loader.EntryFiles.Any(file => File.Exists(Path.Combine(paths.Server, file.ServerName))))
                    ? new ModLaunchPlan(loader.OffArguments)
                    : ModLaunchPlan.None;
            }

            if (loader is null)
            {
                return new ModLaunchPlan([], NoLoader(mods));
            }

            if (await ReadySetAsync(db, row) is not { } set)
            {
                return new ModLaunchPlan([], NoSet(instanceId));
            }

            var setFolder = ModsService.SetFolder(paths, set.Id);
            var carryFrom = set.StartedAt is null && row.RunningSetId is { } running && running != set.Id ? ModsService.SetFolder(paths, running) : null;
            var (copied, carried) = files.Run(() =>
            {
                if (!Directory.Exists(setFolder))
                {
                    throw new IOException($"The folder of mod set {set.Id} is missing ({setFolder}); change the mods to build a new set.");
                }

                return (CopyEntryFiles(loader, setFolder, paths.Server), carryFrom is null ? 0 : CarryConfigs(loader, carryFrom, setFolder));
            });

            if (copied.Count > 0)
            {
                logger.LogInformation("{Instance}: copied {Files} from mod set {Set} into the server folder.", LogText.Clean(instanceId), string.Join(", ", copied), set.Id);
            }

            if (carryFrom is not null)
            {
                logger.LogInformation("{Instance}: mod set {Set} starts for the first time with {Count} config file(s) of set {Previous}.", LogText.Clean(instanceId), set.Id, carried, row.RunningSetId);
            }

            set.StartedAt ??= time.GetUtcNow();
            row.RunningSetId = set.Id;
            await db.SaveChangesAsync();
            return new ModLaunchPlan(loader.OnArguments(setFolder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DbUpdateException)
        {
            logger.LogError(ex, "{Instance}: preparing the mods failed.", LogText.Clean(instanceId));
            return new ModLaunchPlan([], $"Mods: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies each entry file whose content in <c>server\</c> differs from what the set gives, or is missing, so a game
    /// update, a SteamCMD <c>validate</c> or a newer pack is handled. Returns the names copied.
    /// </summary>
    public static IReadOnlyList<string> CopyEntryFiles(IModLoader loader, string setFolder, string serverFolder)
    {
        List<string> copied = [];
        foreach (var file in loader.EntryFiles)
        {
            var source = ModLayout.Resolve(setFolder, file.SetPath);
            if (!File.Exists(source))
            {
                throw new IOException($"The mod set has no {file.SetPath}; change the mods to build a new set.");
            }

            var content = loader.EntryFileContent(file, File.ReadAllBytes(source));
            var target = ModLayout.Resolve(serverFolder, file.ServerName);
            if (File.Exists(target) && SHA256.HashData(File.ReadAllBytes(target)).AsSpan().SequenceEqual(SHA256.HashData(content)))
            {
                continue;
            }

            Directory.CreateDirectory(serverFolder);
            File.WriteAllBytes(target, content);
            copied.Add(file.ServerName);
        }

        return copied;
    }

    /// <summary>
    /// A set's first start (docs/13 §4.7): every file of the previous set's config folder, subfolders included, goes over
    /// the set's own, so what the game or the Owner changed carries over. Returns the number of files copied.
    /// </summary>
    public static int CarryConfigs(IModLoader loader, string fromSetFolder, string toSetFolder)
    {
        var from = Path.Combine(fromSetFolder, loader.ConfigFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(from))
        {
            return 0;
        }

        var to = Path.Combine(toSetFolder, loader.ConfigFolder.Replace('/', Path.DirectorySeparatorChar));
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var target = ModLayout.Resolve(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            count++;
        }

        return count;
    }

    private IModLoader? Loader(AdapterMods mods) => loaders.FirstOrDefault(loader => loader.Id == mods.Loader);

    private static async Task<ModSetRecord?> ReadySetAsync(GameWardenDb db, InstanceModsRecord row) =>
        row.ActiveSetId is { } active ? await db.ModSets.FirstOrDefaultAsync(set => set.Id == active && set.State == ModSetState.Ready) : null;

    private static string NoLoader(AdapterMods mods) => $"Mods are on, but this Core has no mod loader '{mods.Loader}'; turn Mods off to start the game vanilla.";

    private static string NoSet(string instanceId) =>
        $"Mods are on for '{instanceId}', but it has no ready mod set. Add mods (PUT /api/v1/instances/{instanceId}/mods) or turn Mods off.";
}
