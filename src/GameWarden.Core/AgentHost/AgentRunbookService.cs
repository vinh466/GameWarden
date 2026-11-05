using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GameWarden.Core.AgentHost;

public sealed class AgentRunbookDefinition
{
    public string Id { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public AgentRunbookAppliesTo AppliesTo { get; set; } = new();

    public List<AgentRunbookStep> Steps { get; set; } = [];
}

public sealed class AgentRunbookAppliesTo
{
    public string Trigger { get; set; } = "incident";

    public List<string> Topics { get; set; } = [];
}

public sealed class AgentRunbookStep
{
    public string Id { get; set; } = string.Empty;

    public string Use { get; set; } = string.Empty;

    public Dictionary<string, object?>? Args { get; set; }

    public string? When { get; set; }

    /// <summary><c>stop</c> (default) or <c>continue</c>.</summary>
    public string OnError { get; set; } = "stop";
}

public sealed record AgentRunbookOutcome(string RunId, string State, string Output, string? Error);

/// <summary>
/// Executes declarative runbooks (docs/21 §2.8). v1 supports read capabilities and <c>model.summarize</c>; write
/// steps go through the approval engine in a later phase. Every step is checkpointed so a Core restart can resume.
/// </summary>
public sealed partial class AgentRunbookService(
    AgentTools tools,
    AgentModelClient model,
    IDbContextFactory<GameWardenDb> db,
    IOptions<GameWardenOptions> options,
    AgentTrace trace,
    TimeProvider time,
    ILogger<AgentRunbookService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private IReadOnlyDictionary<string, AgentRunbookDefinition>? _definitions;

    public IReadOnlyList<AgentRunbookDefinition> All => [.. Definitions().Values.OrderBy(item => item.Id, StringComparer.Ordinal)];

    public AgentRunbookDefinition? Find(string id) => Definitions().GetValueOrDefault(id);

    public async Task<AgentRunbookOutcome> RunAsync(
        string runbookId,
        string? incidentId,
        string? instanceId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var definition = Find(runbookId) ?? throw new AgentToolException($"No runbook '{runbookId}'.");
        var run = new AgentRunbookRunRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            RunbookId = definition.Id,
            RunbookVersion = definition.Version,
            IncidentId = incidentId,
            InstanceId = instanceId,
            State = "running",
            TraceId = traceId,
            StartedAt = time.GetUtcNow().UtcDateTime,
        };
        await using (var context = await db.CreateDbContextAsync(cancellationToken))
        {
            context.AgentRunbookRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
        }

        var outputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var root = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["incident"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = incidentId,
                ["instanceId"] = instanceId,
            },
            ["runbook"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = definition.Id,
                ["version"] = definition.Version,
            },
        };

        try
        {
            for (var index = 0; index < definition.Steps.Count; index++)
            {
                var step = definition.Steps[index];
                run.CurrentStep = index;
                if (step.When is { Length: > 0 } when && !Truthy(ResolveText(when, root, outputs)))
                {
                    outputs[step.Id] = new JsonObject { ["skipped"] = true };
                    await SaveCheckpointAsync(run, outputs, cancellationToken);
                    continue;
                }

                try
                {
                    outputs[step.Id] = await ExecuteStepAsync(step, root, outputs, cancellationToken);
                }
                catch (Exception ex) when (!IsCancellation(ex) && step.OnError.Equals("continue", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(ex, "Runbook {Runbook} step {Step} continued after an error.", definition.Id, step.Id);
                    outputs[step.Id] = new JsonObject { ["error"] = ex.Message };
                }

                await SaveCheckpointAsync(run, outputs, cancellationToken);
            }

            run.State = "succeeded";
            run.Output = JsonSerializer.Serialize(outputs, Json);
            run.FinishedAt = time.GetUtcNow().UtcDateTime;
            await SaveAsync(run, cancellationToken);
            await trace.AddEventAsync(traceId, "runbook.succeeded", $"Runbook {definition.Id} finished.", instanceId, incidentId: incidentId, cancellationToken: cancellationToken);
            return new AgentRunbookOutcome(run.Id, run.State, run.Output ?? "{}", null);
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            run.State = "failed";
            run.Error = ex.Message;
            run.FinishedAt = time.GetUtcNow().UtcDateTime;
            await SaveAsync(run, cancellationToken);
            logger.LogWarning(ex, "Runbook {Runbook} failed.", definition.Id);
            return new AgentRunbookOutcome(run.Id, run.State, run.Output ?? "{}", ex.Message);
        }
    }

    private async Task<JsonNode?> ExecuteStepAsync(
        AgentRunbookStep step,
        IReadOnlyDictionary<string, object?> root,
        IReadOnlyDictionary<string, JsonNode?> outputs,
        CancellationToken cancellationToken)
    {
        if (step.Use.Equals("model.summarize", StringComparison.OrdinalIgnoreCase))
        {
            var task = ResolveText(Text(step.Args, "task") ?? "Summarize the evidence.", root, outputs);
            var evidence = ResolveText(Text(step.Args, "evidence") ?? "{}", root, outputs);
            var text = await model.SummarizeAsync(task, evidence, cancellationToken);
            return new JsonObject { ["text"] = text };
        }

        var tool = tools.Find(step.Use) ?? throw new AgentToolException($"Runbook step '{step.Id}' uses unknown capability '{step.Use}'.");
        var args = ResolveArgs(step.Args, root, outputs);
        using var document = JsonDocument.Parse(args.ToJsonString());
        var result = await tools.ExecuteAsync(tool, document.RootElement, cancellationToken);
        try
        {
            return JsonNode.Parse(result);
        }
        catch (JsonException)
        {
            return JsonValue.Create(result);
        }
    }

    private JsonObject ResolveArgs(
        IReadOnlyDictionary<string, object?>? args,
        IReadOnlyDictionary<string, object?> root,
        IReadOnlyDictionary<string, JsonNode?> outputs)
    {
        var node = new JsonObject();
        foreach (var (key, value) in args ?? new Dictionary<string, object?>())
        {
            node[key] = value switch
            {
                string text => JsonValue.Create(ResolveText(text, root, outputs)),
                null => null,
                _ => JsonSerializer.SerializeToNode(value, Json),
            };
        }

        return node;
    }

    private string ResolveText(
        string text,
        IReadOnlyDictionary<string, object?> root,
        IReadOnlyDictionary<string, JsonNode?> outputs)
    {
        return Template().Replace(text, match =>
        {
            var path = match.Groups[1].Value.Trim();
            var value = ResolvePath(path, root, outputs);
            return value switch
            {
                null => string.Empty,
                JsonNode node => node.ToJsonString(Json),
                _ => value.ToString() ?? string.Empty,
            };
        });
    }

    private static object? ResolvePath(
        string path,
        IReadOnlyDictionary<string, object?> root,
        IReadOnlyDictionary<string, JsonNode?> outputs)
    {
        var parts = path.Split('.');
        if (parts.Length >= 3 && parts[0] == "steps" && parts[2] == "output")
        {
            var output = outputs.GetValueOrDefault(parts[1]);
            for (var index = 3; index < parts.Length && output is JsonObject obj; index++)
            {
                output = obj[parts[index]];
            }

            return output;
        }

        object? current = root;
        foreach (var part in parts)
        {
            current = current switch
            {
                IReadOnlyDictionary<string, object?> dictionary => dictionary.GetValueOrDefault(part),
                JsonObject obj => obj[part],
                _ => null,
            };
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static bool Truthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("null", StringComparison.OrdinalIgnoreCase);

    private static string? Text(IReadOnlyDictionary<string, object?>? args, string key) =>
        args?.GetValueOrDefault(key)?.ToString();

    private async Task SaveCheckpointAsync(
        AgentRunbookRunRecord run,
        IReadOnlyDictionary<string, JsonNode?> outputs,
        CancellationToken cancellationToken)
    {
        run.Checkpoint = JsonSerializer.Serialize(outputs, Json);
        run.State = "running";
        await SaveAsync(run, cancellationToken);
    }

    private async Task SaveAsync(AgentRunbookRunRecord run, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        context.AgentRunbookRuns.Update(run);
        await context.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyDictionary<string, AgentRunbookDefinition> Definitions()
    {
        if (_definitions is not null)
        {
            return _definitions;
        }

        var found = new Dictionary<string, AgentRunbookDefinition>(StringComparer.Ordinal);
        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "runbooks"), Path.Combine(options.Value.DataRoot, "runbooks") })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.y*ml"))
            {
                try
                {
                    var definition = Deserializer().Deserialize<AgentRunbookDefinition>(File.ReadAllText(file));
                    if (definition is null || string.IsNullOrWhiteSpace(definition.Id) || definition.Steps.Count == 0)
                    {
                        logger.LogWarning("Runbook {File} has no id or steps.", file);
                        continue;
                    }

                    found[definition.Id] = definition;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
                {
                    logger.LogWarning(ex, "Could not read runbook {File}.", file);
                }
            }
        }

        _definitions = found;
        logger.LogInformation("Loaded {Count} Agent runbook(s).", _definitions.Count);
        return _definitions;
    }

    private static IDeserializer Deserializer() =>
        new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

    private static bool IsCancellation(Exception ex) => ex is OperationCanceledException;

    [GeneratedRegex(@"\{\{\s*([^}]+?)\s*\}\}")]
    private static partial Regex Template();
}
