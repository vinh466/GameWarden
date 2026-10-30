namespace GameWarden.Core.Data;

public enum ModSetState
{
    Building,
    Ready,
    Failed,
}

/// <summary>
/// An instance's mods (docs/13 §4.2), table <c>instance_mods</c>: one row per instance that has had a mods job.
/// The pointers name rows of <c>mod_sets</c>; any set none of them names is deleted on the next activation.
/// </summary>
public sealed class InstanceModsRecord
{
    public required string InstanceId { get; set; }

    /// <summary><i>Mods on</i>: the next start loads the active set (phase 1b).</summary>
    public bool Enabled { get; set; }

    /// <summary>The set the next start uses.</summary>
    public long? ActiveSetId { get; set; }

    /// <summary>One step back: <c>POST /mods/rollback</c> swaps it with the active set.</summary>
    public long? PreviousSetId { get; set; }

    /// <summary>The set the running server loaded; set by the launch path from phase 1b, null until then.</summary>
    public long? RunningSetId { get; set; }

    /// <summary>The last r2modman share code made from the active set (phase 1c).</summary>
    public string? ShareCode { get; set; }

    public DateTimeOffset? ShareCodeCreatedAt { get; set; }
}

/// <summary>One exact list of packages, built into <c>{paths.mods}\sets\&lt;id&gt;\</c>; never changed once it is ready. Table <c>mod_sets</c>.</summary>
public sealed class ModSetRecord
{
    public long Id { get; set; }

    public required string InstanceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary><c>code</c>, <c>edit</c>, <c>update</c> or <c>rollback</c>; phase 1a makes <c>edit</c> sets only.</summary>
    public required string Origin { get; set; }

    public ModSetState State { get; set; }

    /// <summary>The <c>mods.sync</c> job that built it.</summary>
    public string? JobId { get; set; }
}

/// <summary>A package of a set, table <c>mod_entries</c>.</summary>
public sealed class ModEntryRecord
{
    public long SetId { get; set; }

    /// <summary><c>Namespace-Name</c>, as Thunderstore writes it.</summary>
    public required string Package { get; set; }

    /// <summary><c>x.y.z</c>.</summary>
    public required string Version { get; set; }

    /// <summary>A disabled mod stays in the set and is neither downloaded nor installed.</summary>
    public bool Enabled { get; set; }

    /// <summary><c>chosen</c> (from a code or picked by the Owner; its version never changes) or <c>dependency</c>.</summary>
    public required string Origin { get; set; }
}

/// <summary>
/// The trusted hash of a package version, for every instance (table <c>package_versions</c>): the first download records
/// it and every later download and build must match (trust on first use, docs/13 §4.5).
/// </summary>
public sealed class PackageVersionRecord
{
    public required string Package { get; set; }

    public required string Version { get; set; }

    /// <summary>Lowercase hex.</summary>
    public required string Sha256 { get; set; }

    /// <summary>Bytes of the zip as first downloaded.</summary>
    public long Size { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
}
