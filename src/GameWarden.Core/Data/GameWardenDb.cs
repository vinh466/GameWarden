using GameWarden.Core.Instances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameWarden.Core.Data;

/// <summary>SQLite database, the source of truth (OQ-17). Schema changes go through EF Core migrations.</summary>
public sealed class GameWardenDb(DbContextOptions<GameWardenDb> options) : DbContext(options)
{
    public DbSet<InstanceRecord> Instances => Set<InstanceRecord>();

    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    public DbSet<UserRecord> Users => Set<UserRecord>();

    public DbSet<ActivityRecord> Activity => Set<ActivityRecord>();

    public DbSet<InstanceModsRecord> InstanceMods => Set<InstanceModsRecord>();

    public DbSet<ModSetRecord> ModSets => Set<ModSetRecord>();

    public DbSet<ModEntryRecord> ModEntries => Set<ModEntryRecord>();

    public DbSet<PackageVersionRecord> PackageVersions => Set<PackageVersionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var instance = modelBuilder.Entity<InstanceRecord>();
        instance.ToTable("instances");
        instance.HasKey(i => i.Id);
        instance.Property(i => i.Id).HasMaxLength(64);
        instance.Property(i => i.AdapterId).HasMaxLength(64);
        instance.Property(i => i.DesiredState).HasConversion<string>().HasMaxLength(16);
        instance.Property(i => i.PersistedState).HasConversion<string>().HasMaxLength(16);

        var job = modelBuilder.Entity<JobRecord>();
        job.ToTable("jobs");
        job.HasKey(j => j.Id);
        job.Property(j => j.Id).HasMaxLength(64);
        job.Property(j => j.Type).HasMaxLength(32);
        job.Property(j => j.InstanceId).HasMaxLength(64);
        job.Property(j => j.State).HasConversion<string>().HasMaxLength(16);
        job.Property(j => j.Stage).HasMaxLength(64);
        job.HasIndex(j => j.InstanceId);

        var user = modelBuilder.Entity<UserRecord>();
        user.ToTable("users");
        user.HasKey(u => u.Id);
        user.Property(u => u.Id).HasMaxLength(64);
        user.Property(u => u.UserName).HasMaxLength(64).UseCollation("NOCASE");
        user.HasIndex(u => u.UserName).IsUnique();
        user.Property(u => u.SecurityStamp).HasMaxLength(64);

        var activity = modelBuilder.Entity<ActivityRecord>();
        activity.ToTable("events");
        activity.HasKey(a => a.Id);
        activity.Property(a => a.Kind).HasMaxLength(32);
        activity.Property(a => a.Level).HasMaxLength(8);
        activity.Property(a => a.InstanceId).HasMaxLength(64);
        activity.Property(a => a.JobId).HasMaxLength(64);
        activity.Property(a => a.User).HasMaxLength(64);
        activity.Property(a => a.Message).HasMaxLength(1024);
        activity.HasIndex(a => a.InstanceId);

        // Mods (docs/13 §4.2): an instance's rows go with it; the set pointers are plain ids, kept consistent by ModsService.
        var mods = modelBuilder.Entity<InstanceModsRecord>();
        mods.ToTable("instance_mods");
        mods.HasKey(m => m.InstanceId);
        mods.Property(m => m.InstanceId).HasMaxLength(64);
        mods.Property(m => m.ShareCode).HasMaxLength(64);
        mods.HasOne<InstanceRecord>().WithMany().HasForeignKey(m => m.InstanceId).OnDelete(DeleteBehavior.Cascade);

        var modSet = modelBuilder.Entity<ModSetRecord>();
        modSet.ToTable("mod_sets");
        modSet.HasKey(s => s.Id);
        modSet.Property(s => s.InstanceId).HasMaxLength(64);
        modSet.Property(s => s.Origin).HasMaxLength(16);
        modSet.Property(s => s.State).HasConversion<string>().HasMaxLength(16);
        modSet.Property(s => s.JobId).HasMaxLength(64);
        modSet.HasIndex(s => s.InstanceId);
        modSet.HasOne<InstanceRecord>().WithMany().HasForeignKey(s => s.InstanceId).OnDelete(DeleteBehavior.Cascade);

        var modEntry = modelBuilder.Entity<ModEntryRecord>();
        modEntry.ToTable("mod_entries");
        modEntry.HasKey(e => new { e.SetId, e.Package });
        modEntry.Property(e => e.Package).HasMaxLength(128);
        modEntry.Property(e => e.Version).HasMaxLength(32);
        modEntry.Property(e => e.Origin).HasMaxLength(16);
        modEntry.HasOne<ModSetRecord>().WithMany().HasForeignKey(e => e.SetId).OnDelete(DeleteBehavior.Cascade);

        var packageVersion = modelBuilder.Entity<PackageVersionRecord>();
        packageVersion.ToTable("package_versions");
        packageVersion.HasKey(p => new { p.Package, p.Version });
        packageVersion.Property(p => p.Package).HasMaxLength(128);
        packageVersion.Property(p => p.Version).HasMaxLength(32);
        packageVersion.Property(p => p.Sha256).HasMaxLength(64);
    }
}

public sealed class InstanceRecord
{
    public required string Id { get; set; }

    public required string AdapterId { get; set; }

    public required string AdapterVersion { get; set; }

    public required string DisplayName { get; set; }

    public DesiredState DesiredState { get; set; }

    /// <summary>States that must survive a restart: <c>NotInstalled</c>, <c>Stopped</c>, <c>Faulted</c> (OQ-16).</summary>
    public ActualState PersistedState { get; set; }

    /// <summary>Runner of the running instance: PID + start time guard against PID reuse (02 §4.2).</summary>
    public int? RunnerPid { get; set; }

    public DateTimeOffset? RunnerStartedAt { get; set; }

    public string PortsJson { get; set; } = "{}";

    public string SettingsJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Used by <c>dotnet ef</c> only.</summary>
internal sealed class DesignTimeDbFactory : IDesignTimeDbContextFactory<GameWardenDb>
{
    public GameWardenDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GameWardenDb>().UseSqlite("Data Source=design-time.db").Options);
}
