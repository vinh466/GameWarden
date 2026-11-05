using System.Diagnostics;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// Correlation for one Agent turn (docs/21 §2.9). The trace id is written next to runs, tool calls, approvals,
/// incidents and events, so a Discord message can be followed through the model, the policy and the job. The
/// ActivitySource is standard OpenTelemetry; exporting it is optional.
/// </summary>
public sealed class AgentTrace(
    IDbContextFactory<GameWardenDb> db,
    TimeProvider time,
    ILogger<AgentTrace> logger)
{
    public const string SourceName = "GameWarden.Agent";

    private static readonly ActivitySource Source = new(SourceName, "1.0.0");

    public string NewTraceId() =>
        System.Diagnostics.Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");

    public string NewTurnId() => Guid.NewGuid().ToString("N");

    public System.Diagnostics.Activity? Start(string name, string traceId, params (string Key, object? Value)[] tags)
    {
        var activity = Source.StartActivity(name, System.Diagnostics.ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("agent.trace_id", traceId);
        foreach (var (key, value) in tags)
        {
            activity.SetTag(key, value);
        }

        return activity;
    }

    /// <summary>Append a redacted event to the trace timeline. A database problem never fails the turn.</summary>
    public async Task AddEventAsync(
        string traceId,
        string type,
        string text,
        string? instanceId = null,
        long? runId = null,
        string? incidentId = null,
        string? data = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            context.AgentEvents.Add(new AgentEventRecord
            {
                At = time.GetUtcNow().UtcDateTime,
                TraceId = traceId,
                Type = type,
                Text = text.Length > 4000 ? text[..4000] : text,
                Data = data,
                InstanceId = instanceId,
                RunId = runId,
                IncidentId = incidentId,
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not store Agent trace event {Type}.", type);
        }
    }
}
