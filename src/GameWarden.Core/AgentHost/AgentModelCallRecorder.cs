using GameWarden.Core.Agents;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.AgentHost;

public sealed record AgentModelCallInput(
    string Purpose,
    string Model,
    string? TraceId,
    long? RunId,
    string? TurnId,
    string? IncidentId,
    int LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int ToolCalls,
    string Status,
    string? Error,
    string? Prompt,
    string? Response);

/// <summary>
/// Stores a redacted, truncated preview of every model request/response for the panel's observability view
/// (docs/21 §2.9). A storage failure never fails a turn.
/// </summary>
public sealed class AgentModelCallRecorder(
    IDbContextFactory<GameWardenDb> db,
    AgentRedactor redactor,
    TimeProvider time,
    ILogger<AgentModelCallRecorder> logger)
{
    public async Task RecordAsync(AgentModelCallInput call, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            context.AgentModelCalls.Add(new AgentModelCallRecord
            {
                At = time.GetUtcNow().UtcDateTime,
                TraceId = call.TraceId,
                RunId = call.RunId,
                TurnId = call.TurnId,
                Purpose = call.Purpose,
                Model = call.Model.Length > 128 ? call.Model[..128] : call.Model,
                LatencyMs = call.LatencyMs,
                PromptTokens = call.PromptTokens,
                CompletionTokens = call.CompletionTokens,
                ToolCalls = call.ToolCalls,
                Status = call.Status,
                Error = Clip(call.Error, 2000),
                PromptPreview = Preview(call.Prompt),
                ResponsePreview = Preview(call.Response),
                IncidentId = call.IncidentId,
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not store Agent model call metadata.");
        }
    }

    private string? Preview(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var redacted = redactor.Redact(text);
        return redacted.Length <= 8000 ? redacted : redacted[..8000] + "…";
    }

    private static string? Clip(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max] + "…";
}
