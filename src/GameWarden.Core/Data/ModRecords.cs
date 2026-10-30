namespace GameWarden.Core.Data;

/// <summary>How a config revision came (docs/13 §4.7).</summary>
public enum ModConfigRevisionKind
{
    /// <summary>What was on disk before a save, when it differed from the newest revision: the game, a mod update or a code wrote it.</summary>
    Server,

    /// <summary>The Owner saved it in the panel.</summary>
    Edit,

    /// <summary>The Owner restored an older revision.</summary>
    Restore,
}

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

    /// <summary><i>Mods on</i>: the next start loads the active set; off, it runs the game vanilla (docs/13 §4.3).</summary>
    public bool Enabled { get; set; }

    /// <summary>The set the next start uses.</summary>
    public long? ActiveSetId { get; set; }

    /// <summary>One step back: <c>POST /mods/rollback</c> swaps it with the active set.</summary>
    public long? PreviousSetId { get; set; }

    /// <summary>
    /// The set the last start with <i>Mods on</i> loaded, recorded by the launch path (docs/13 §4.3); never pruned.
    /// </summary>
    public long? RunningSetId { get; set; }

    /// <summary>
    /// How the last start went, recorded by the launch path: true with <i>Mods on</i> (it loaded
    /// <see cref="RunningSetId"/>), false vanilla; null until a start records it. With the active set and <i>Mods on</i>
    /// it tells whether a restart would load something else (<c>restartRequired</c>, phase 2).
    /// </summary>
    public bool? LastStartModsOn { get; set; }

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

/// <summary>
/// One revision of a mod config file (docs/13 §4.7, ADR-0021), table <c>mod_config_revisions</c>: per instance and path,
/// so revisions survive set changes. The newest <see cref="Mods.ModsService.ConfigRevisionsKept"/> per file are kept.
/// </summary>
public sealed class ModConfigRevisionRecord
{
    public long Id { get; set; }

    public required string InstanceId { get; set; }

    /// <summary>Relative to the loader's config folder (<c>BepInEx/config</c>), with <c>/</c> separators.</summary>
    public required string Path { get; set; }

    public ModConfigRevisionKind Kind { get; set; }

    /// <summary>Of <see cref="Content"/>, lowercase hex.</summary>
    public required string Sha256 { get; set; }

    public long Size { get; set; }

    /// <summary>The file's bytes as they were on disk or as they were written.</summary>
    public required byte[] Content { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
