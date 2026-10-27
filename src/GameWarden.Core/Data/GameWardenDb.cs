using GameWarden.Core.Instances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameWarden.Core.Data;

/// <summary>SQLite database, the source of truth (OQ-17). Schema changes go through EF Core migrations.</summary>
public sealed class GameWardenDb(DbContextOptions<GameWardenDb> options) : DbContext(options)
{
    public DbSet<InstanceRecord> Instances => Set<InstanceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var instance = modelBuilder.Entity<InstanceRecord>();
        instance.ToTable("instances");
        instance.HasKey(i => i.Id);
        instance.Property(i => i.Id).HasMaxLength(64);
        instance.Property(i => i.AdapterId).HasMaxLength(64);
        instance.Property(i => i.DesiredState).HasConversion<string>().HasMaxLength(16);
        instance.Property(i => i.PersistedState).HasConversion<string>().HasMaxLength(16);
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
