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

    public DbSet<SystemLogRecord> SystemLog => Set<SystemLogRecord>();

    public DbSet<NotificationEventRecord> NotificationEvents => Set<NotificationEventRecord>();

    public DbSet<NotificationDeliveryRecord> NotificationDeliveries => Set<NotificationDeliveryRecord>();

    public DbSet<NotificationStatusRecord> NotificationStatuses => Set<NotificationStatusRecord>();

    public DbSet<InstanceModsRecord> InstanceMods => Set<InstanceModsRecord>();

    public DbSet<ModSetRecord> ModSets => Set<ModSetRecord>();

    public DbSet<ModEntryRecord> ModEntries => Set<ModEntryRecord>();

    public DbSet<ModConfigRevisionRecord> ModConfigRevisions => Set<ModConfigRevisionRecord>();

    public DbSet<PackageVersionRecord> PackageVersions => Set<PackageVersionRecord>();

    public DbSet<AgentClientRecord> AgentClients => Set<AgentClientRecord>();

    public DbSet<AgentTokenRecord> AgentTokens => Set<AgentTokenRecord>();

    public DbSet<AgentSettingsRecord> AgentSettings => Set<AgentSettingsRecord>();

    public DbSet<AgentAuditRecord> AgentAudit => Set<AgentAuditRecord>();

    public DbSet<AgentConversationRecord> AgentConversations => Set<AgentConversationRecord>();

    public DbSet<AgentMessageRecord> AgentMessages => Set<AgentMessageRecord>();

    public DbSet<AgentRunRecord> AgentRuns => Set<AgentRunRecord>();

    public DbSet<AgentToolCallRecord> AgentToolCalls => Set<AgentToolCallRecord>();

    public DbSet<AgentApprovalRecord> AgentApprovals => Set<AgentApprovalRecord>();

    public DbSet<AgentIncidentRecord> AgentIncidents => Set<AgentIncidentRecord>();

    public DbSet<AgentIncidentEventRecord> AgentIncidentEvents => Set<AgentIncidentEventRecord>();

    public DbSet<AgentRunbookRunRecord> AgentRunbookRuns => Set<AgentRunbookRunRecord>();

    public DbSet<AgentPolicyDecisionRecord> AgentPolicyDecisions => Set<AgentPolicyDecisionRecord>();

    public DbSet<AgentEventRecord> AgentEvents => Set<AgentEventRecord>();

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

        var notificationEvent = modelBuilder.Entity<NotificationEventRecord>();
        notificationEvent.ToTable("notification_events");
        notificationEvent.HasKey(row => row.Id);
        notificationEvent.Property(row => row.Topic).HasMaxLength(64);
        notificationEvent.Property(row => row.Severity).HasMaxLength(16);
        notificationEvent.Property(row => row.Title).HasMaxLength(256);
        notificationEvent.Property(row => row.Body).HasMaxLength(4000);
        notificationEvent.Property(row => row.FieldsJson).HasMaxLength(8000);
        notificationEvent.Property(row => row.InstanceId).HasMaxLength(64);
        notificationEvent.Property(row => row.JobId).HasMaxLength(64);
        notificationEvent.Property(row => row.Url).HasMaxLength(1024);
        notificationEvent.Property(row => row.DedupeKey).HasMaxLength(256);
        notificationEvent.Property(row => row.Source).HasMaxLength(32);
        notificationEvent.HasIndex(row => row.At);
        notificationEvent.HasIndex(row => row.DedupeKey);

        var notificationDelivery = modelBuilder.Entity<NotificationDeliveryRecord>();
        notificationDelivery.ToTable("notification_deliveries");
        notificationDelivery.HasKey(row => row.Id);
        notificationDelivery.Property(row => row.DestinationId).HasMaxLength(64);
        notificationDelivery.Property(row => row.Mode).HasMaxLength(16);
        notificationDelivery.Property(row => row.State).HasMaxLength(16);
        notificationDelivery.Property(row => row.LastError).HasMaxLength(2048);
        notificationDelivery.Property(row => row.MessageId).HasMaxLength(64);
        notificationDelivery.HasIndex(row => new { row.EventId, row.DestinationId }).IsUnique();
        notificationDelivery.HasIndex(row => new { row.State, row.NextAttemptAt });

        var notificationStatus = modelBuilder.Entity<NotificationStatusRecord>();
        notificationStatus.ToTable("notification_status");
        notificationStatus.HasKey(row => row.DestinationId);
        notificationStatus.Property(row => row.DestinationId).HasMaxLength(64);
        notificationStatus.Property(row => row.MessageId).HasMaxLength(64);
        notificationStatus.Property(row => row.Content).HasMaxLength(4000);

        var systemLog = modelBuilder.Entity<SystemLogRecord>();
        systemLog.ToTable("system_log");
        systemLog.HasKey(row => row.Id);
        systemLog.Property(row => row.Level).HasMaxLength(16);
        systemLog.Property(row => row.Component).HasMaxLength(256);
        systemLog.Property(row => row.Category).HasMaxLength(256);
        systemLog.Property(row => row.Message).HasMaxLength(4000);
        systemLog.Property(row => row.Exception).HasMaxLength(8000);
        systemLog.Property(row => row.InstanceId).HasMaxLength(64);
        systemLog.Property(row => row.JobId).HasMaxLength(64);
        systemLog.Property(row => row.RequestId).HasMaxLength(128);
        systemLog.Property(row => row.AgentClientId).HasMaxLength(64);
        systemLog.Property(row => row.Source).HasMaxLength(32);
        systemLog.Property(row => row.Data).HasMaxLength(8000);
        systemLog.HasIndex(row => row.At);
        systemLog.HasIndex(row => new { row.Level, row.At });
        systemLog.HasIndex(row => new { row.Component, row.At });
        systemLog.HasIndex(row => new { row.InstanceId, row.At });
        systemLog.HasIndex(row => new { row.JobId, row.At });
        systemLog.HasIndex(row => row.RequestId);

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

        // Config revisions (docs/13 §4.7, ADR-0021): per instance and path, gone with the instance.
        var configRevision = modelBuilder.Entity<ModConfigRevisionRecord>();
        configRevision.ToTable("mod_config_revisions");
        configRevision.HasKey(r => r.Id);
        configRevision.Property(r => r.InstanceId).HasMaxLength(64);
        configRevision.Property(r => r.Path).HasMaxLength(1024);
        configRevision.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        configRevision.Property(r => r.Sha256).HasMaxLength(64);
        configRevision.HasIndex(r => new { r.InstanceId, r.Path });
        configRevision.HasOne<InstanceRecord>().WithMany().HasForeignKey(r => r.InstanceId).OnDelete(DeleteBehavior.Cascade);

        var packageVersion = modelBuilder.Entity<PackageVersionRecord>();
        packageVersion.ToTable("package_versions");
        packageVersion.HasKey(p => new { p.Package, p.Version });
        packageVersion.Property(p => p.Package).HasMaxLength(128);
        packageVersion.Property(p => p.Version).HasMaxLength(32);
        packageVersion.Property(p => p.Sha256).HasMaxLength(64);

        // Agents (docs/15): clients own tokens; audit rows keep the client id as text so deleting a client does not
        // erase its history.
        var agentClient = modelBuilder.Entity<AgentClientRecord>();
        agentClient.ToTable("agent_clients");
        agentClient.HasKey(c => c.Id);
        agentClient.Property(c => c.Id).HasMaxLength(64);
        agentClient.Property(c => c.Name).HasMaxLength(64).UseCollation("NOCASE");
        agentClient.HasIndex(c => c.Name).IsUnique();

        var agentToken = modelBuilder.Entity<AgentTokenRecord>();
        agentToken.ToTable("agent_tokens");
        agentToken.HasKey(t => t.Id);
        agentToken.Property(t => t.Id).HasMaxLength(64);
        agentToken.Property(t => t.ClientId).HasMaxLength(64);
        agentToken.Property(t => t.Name).HasMaxLength(64);
        agentToken.Property(t => t.SecretHash).HasMaxLength(64);
        agentToken.Property(t => t.Prefix).HasMaxLength(64);
        agentToken.HasIndex(t => t.ClientId);
        agentToken.HasOne<AgentClientRecord>().WithMany().HasForeignKey(t => t.ClientId).OnDelete(DeleteBehavior.Cascade);

        var agentSettings = modelBuilder.Entity<AgentSettingsRecord>();
        agentSettings.ToTable("agent_settings");
        agentSettings.HasKey(s => s.Id);

        var agentAudit = modelBuilder.Entity<AgentAuditRecord>();
        agentAudit.ToTable("agent_audit");
        agentAudit.HasKey(a => a.Id);
        agentAudit.Property(a => a.ClientId).HasMaxLength(64);
        agentAudit.Property(a => a.ClientName).HasMaxLength(64);
        agentAudit.Property(a => a.TokenId).HasMaxLength(64);
        agentAudit.Property(a => a.Action).HasMaxLength(64);
        agentAudit.Property(a => a.Target).HasMaxLength(128);
        agentAudit.Property(a => a.Outcome).HasMaxLength(16);
        agentAudit.Property(a => a.SourceIp).HasMaxLength(64);
        agentAudit.Property(a => a.RequestId).HasMaxLength(64);
        agentAudit.Property(a => a.Details).HasMaxLength(4096);
        agentAudit.HasIndex(a => a.ClientId);
        agentAudit.HasIndex(a => a.At);

        var conversation = modelBuilder.Entity<AgentConversationRecord>();
        conversation.ToTable("agent_conversations");
        conversation.HasKey(row => row.Id);
        conversation.Property(row => row.Id).HasMaxLength(64);
        conversation.Property(row => row.InstanceId).HasMaxLength(64);
        conversation.HasIndex(row => row.UpdatedAt);

        var message = modelBuilder.Entity<AgentMessageRecord>();
        message.ToTable("agent_messages");
        message.HasKey(row => row.Id);
        message.Property(row => row.ConversationId).HasMaxLength(64);
        message.Property(row => row.Role).HasMaxLength(16);
        message.Property(row => row.Content).HasMaxLength(8000);
        message.HasIndex(row => new { row.ConversationId, row.Id });

        var run = modelBuilder.Entity<AgentRunRecord>();
        run.ToTable("agent_runs");
        run.HasKey(row => row.Id);
        run.Property(row => row.ConversationId).HasMaxLength(64);
        run.Property(row => row.Model).HasMaxLength(128);
        run.Property(row => row.State).HasMaxLength(16);
        run.Property(row => row.Error).HasMaxLength(2048);
        run.Property(row => row.TraceId).HasMaxLength(64);
        run.Property(row => row.TurnId).HasMaxLength(64);
        run.Property(row => row.PromptId).HasMaxLength(64);
        run.HasIndex(row => row.StartedAt);

        var call = modelBuilder.Entity<AgentToolCallRecord>();
        call.ToTable("agent_tool_calls");
        call.HasKey(row => row.Id);
        call.Property(row => row.Tool).HasMaxLength(64);
        call.Property(row => row.Risk).HasMaxLength(16);
        call.Property(row => row.State).HasMaxLength(16);
        call.Property(row => row.TraceId).HasMaxLength(64);
        call.Property(row => row.PolicyRule).HasMaxLength(64);
        call.Property(row => row.ApprovalId).HasMaxLength(64);
        call.Property(row => row.InstanceId).HasMaxLength(64);
        call.Property(row => row.ErrorCode).HasMaxLength(64);
        call.HasIndex(row => row.RunId);

        var approval = modelBuilder.Entity<AgentApprovalRecord>();
        approval.ToTable("agent_approvals");
        approval.HasKey(row => row.Id);
        approval.Property(row => row.Id).HasMaxLength(64);
        approval.Property(row => row.ConversationId).HasMaxLength(64);
        approval.Property(row => row.Tool).HasMaxLength(64);
        approval.Property(row => row.State).HasMaxLength(16);
        approval.Property(row => row.DecidedBy).HasMaxLength(64);
        approval.Property(row => row.MessageId).HasMaxLength(64);
        approval.Property(row => row.TraceId).HasMaxLength(64);
        approval.Property(row => row.PolicyRule).HasMaxLength(64);
        approval.Property(row => row.Impact).HasMaxLength(2000);
        approval.Property(row => row.Scope).HasMaxLength(64);
        approval.Property(row => row.Source).HasMaxLength(16);
        approval.Property(row => row.SourceId).HasMaxLength(64);
        approval.Property(row => row.InstanceId).HasMaxLength(64);
        approval.Property(row => row.ExecutionState).HasMaxLength(16);
        approval.Property(row => row.Verification).HasMaxLength(2000);
        approval.HasIndex(row => new { row.State, row.ExpiresAt });

        var incident = modelBuilder.Entity<AgentIncidentRecord>();
        incident.ToTable("agent_incidents");
        incident.HasKey(row => row.Id);
        incident.Property(row => row.Id).HasMaxLength(64);
        incident.Property(row => row.InstanceId).HasMaxLength(64);
        incident.Property(row => row.Severity).HasMaxLength(16);
        incident.Property(row => row.State).HasMaxLength(24);
        incident.Property(row => row.Title).HasMaxLength(256);
        incident.Property(row => row.Summary).HasMaxLength(4000);
        incident.Property(row => row.Trigger).HasMaxLength(32);
        incident.Property(row => row.TriggerId).HasMaxLength(64);
        incident.Property(row => row.ChannelId).HasMaxLength(64);
        incident.Property(row => row.ThreadId).HasMaxLength(64);
        incident.Property(row => row.MessageId).HasMaxLength(64);
        incident.Property(row => row.TraceId).HasMaxLength(64);
        incident.HasIndex(row => new { row.InstanceId, row.State });
        incident.HasIndex(row => row.OpenedAt);

        var incidentEvent = modelBuilder.Entity<AgentIncidentEventRecord>();
        incidentEvent.ToTable("agent_incident_events");
        incidentEvent.HasKey(row => row.Id);
        incidentEvent.Property(row => row.IncidentId).HasMaxLength(64);
        incidentEvent.Property(row => row.Type).HasMaxLength(32);
        incidentEvent.Property(row => row.Text).HasMaxLength(4000);
        incidentEvent.Property(row => row.TraceId).HasMaxLength(64);
        incidentEvent.HasIndex(row => new { row.IncidentId, row.Id });

        var runbookRun = modelBuilder.Entity<AgentRunbookRunRecord>();
        runbookRun.ToTable("agent_runbook_runs");
        runbookRun.HasKey(row => row.Id);
        runbookRun.Property(row => row.Id).HasMaxLength(64);
        runbookRun.Property(row => row.RunbookId).HasMaxLength(64);
        runbookRun.Property(row => row.IncidentId).HasMaxLength(64);
        runbookRun.Property(row => row.InstanceId).HasMaxLength(64);
        runbookRun.Property(row => row.State).HasMaxLength(24);
        runbookRun.Property(row => row.Checkpoint).HasMaxLength(8000);
        runbookRun.Property(row => row.Output).HasMaxLength(8000);
        runbookRun.Property(row => row.Error).HasMaxLength(2000);
        runbookRun.Property(row => row.TraceId).HasMaxLength(64);
        runbookRun.HasIndex(row => row.IncidentId);

        var policyDecision = modelBuilder.Entity<AgentPolicyDecisionRecord>();
        policyDecision.ToTable("agent_policy_decisions");
        policyDecision.HasKey(row => row.Id);
        policyDecision.Property(row => row.Capability).HasMaxLength(64);
        policyDecision.Property(row => row.Action).HasMaxLength(64);
        policyDecision.Property(row => row.InstanceId).HasMaxLength(64);
        policyDecision.Property(row => row.Rule).HasMaxLength(64);
        policyDecision.Property(row => row.Decision).HasMaxLength(24);
        policyDecision.Property(row => row.Reason).HasMaxLength(2000);
        policyDecision.Property(row => row.InputHash).HasMaxLength(64);
        policyDecision.Property(row => row.TraceId).HasMaxLength(64);
        policyDecision.Property(row => row.TurnId).HasMaxLength(64);
        policyDecision.HasIndex(row => row.At);

        var agentEvent = modelBuilder.Entity<AgentEventRecord>();
        agentEvent.ToTable("agent_events");
        agentEvent.HasKey(row => row.Id);
        agentEvent.Property(row => row.TraceId).HasMaxLength(64);
        agentEvent.Property(row => row.Type).HasMaxLength(64);
        agentEvent.Property(row => row.Text).HasMaxLength(4000);
        agentEvent.Property(row => row.InstanceId).HasMaxLength(64);
        agentEvent.Property(row => row.IncidentId).HasMaxLength(64);
        agentEvent.HasIndex(row => new { row.TraceId, row.Id });
        agentEvent.HasIndex(row => row.At);
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
