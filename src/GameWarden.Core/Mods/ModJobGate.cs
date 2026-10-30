using System.Collections.Concurrent;

namespace GameWarden.Core.Mods;

/// <summary>
/// One mods job per instance (docs/13 §4.5): a <c>mods.sync</c> or a rollback holds its instance's place until it ends,
/// and a purge takes it too, so an instance is not purged while its mods change. Separate from the instance lock,
/// which a mods job does not take.
/// </summary>
public sealed class ModJobGate
{
    private readonly ConcurrentDictionary<string, string> _held = new(StringComparer.Ordinal);

    /// <param name="holder">What holds it, for the refusal message: a job id, <c>rollback</c> or <c>purge</c>.</param>
    public bool TryEnter(string instanceId, string holder) => _held.TryAdd(instanceId, holder);

    public void Exit(string instanceId) => _held.TryRemove(instanceId, out _);

    public string? Holder(string instanceId) => _held.GetValueOrDefault(instanceId);

    /// <summary>The refusal when <see cref="TryEnter"/> fails.</summary>
    public string BusyMessage(string instanceId) => Holder(instanceId) switch
    {
        null or "rollback" or "purge" => $"The mods of '{instanceId}' are changing; try again in a moment.",
        { } jobId => $"A mods job ({jobId}) is running for '{instanceId}'; wait for it to finish.",
    };
}
