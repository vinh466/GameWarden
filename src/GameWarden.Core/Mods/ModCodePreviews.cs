using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace GameWarden.Core.Mods;

/// <summary>A code read for an instance: its mods as the code has them and its config files, until it expires.</summary>
public sealed record ModCodePreviewData(
    string Id,
    string InstanceId,
    string Code,
    string ProfileName,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<R2ModmanMod> Mods,
    IReadOnlyList<CodeConfigFile> ConfigFiles);

/// <summary>
/// Previews of r2modman codes (docs/13 §4.4), kept in memory for <see cref="Lifetime"/> under an id nobody can guess and
/// bound to their instance: <c>PUT /mods</c> with a <c>previewId</c> applies exactly what the Owner saw. A Core restart
/// forgets them. At most <see cref="MaxPreviews"/> are kept, the oldest going first, since each holds its config files.
/// </summary>
public sealed class ModCodePreviews(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public const int MaxPreviews = 20;

    private readonly ConcurrentDictionary<string, ModCodePreviewData> _previews = new(StringComparer.Ordinal);

    public ModCodePreviewData Add(string instanceId, string code, R2ModmanProfile profile)
    {
        var now = time.GetUtcNow();
        var preview = new ModCodePreviewData(
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            instanceId,
            code,
            profile.ProfileName,
            now + Lifetime,
            profile.Mods,
            profile.ConfigFiles);
        Prune(now);
        while (_previews.Count >= MaxPreviews && _previews.Values.MinBy(kept => kept.ExpiresAt) is { } oldest)
        {
            _previews.TryRemove(oldest.Id, out _);
        }

        _previews[preview.Id] = preview;
        return preview;
    }

    /// <summary>The preview, when it exists, has not expired and belongs to <paramref name="instanceId"/>.</summary>
    public ModCodePreviewData? Find(string instanceId, string previewId)
    {
        Prune(time.GetUtcNow());
        return _previews.TryGetValue(previewId, out var preview) && preview.InstanceId == instanceId ? preview : null;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var expired in _previews.Values.Where(preview => preview.ExpiresAt <= now))
        {
            _previews.TryRemove(expired.Id, out _);
        }
    }
}
