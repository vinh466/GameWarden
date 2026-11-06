using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.AgentHost;

public sealed class AgentEvalScenario
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string Context { get; set; } = string.Empty;

    /// <summary>Fake tool output by tool name; <c>search_knowledge</c> uses the real local knowledge service.</summary>
    public Dictionary<string, JsonElement> Tools { get; set; } = new(StringComparer.Ordinal);

    public AgentEvalExpect Expect { get; set; } = new();
}

public sealed class AgentEvalExpect
{
    public List<string> Tools { get; set; } = [];

    /// <summary>At least one of these tool names must be used (use when a query has two valid routes).</summary>
    public List<string> AnyTools { get; set; } = [];

    public List<string> ForbiddenTools { get; set; } = [];

    public List<string> AnswerContains { get; set; } = [];

    /// <summary>At least one of these substrings must appear in the answer.</summary>
    public List<string> AnswerContainsAny { get; set; } = [];

    public List<string> AnswerNotContains { get; set; } = [];

    public int MaxToolCalls { get; set; } = 6;
}

public sealed record AgentEvalCheck(string Name, bool Ok, string? Detail);

public sealed record AgentEvalCaseResult(
    string Id,
    string Name,
    bool Passed,
    int Score,
    IReadOnlyList<AgentEvalCheck> Checks,
    IReadOnlyList<string> ToolCalls,
    string Answer,
    long LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    string? Error);

public sealed record AgentEvalRunResult(
    string Model,
    DateTimeOffset At,
    int Passed,
    int Total,
    IReadOnlyList<AgentEvalCaseResult> Cases);

/// <summary>
/// Golden scenarios for model quality (docs/21 §5.7). The model runs against fake tool outputs, so an eval never
/// touches a real server, a real job or the secret store. The Owner triggers it explicitly.
/// </summary>
public sealed class AgentEvalService(
    AgentTools tools,
    AgentModelClient model,
    AgentHostSettingsService settings,
    IOptions<GameWardenOptions> options,
    ILogger<AgentEvalService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string HistoryPath => Path.Combine(options.Value.DataRoot, "agent-evals.json");

    public IReadOnlyList<AgentEvalScenario> Scenarios()
    {
        var found = new Dictionary<string, AgentEvalScenario>(StringComparer.Ordinal);
        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "evals"), Path.Combine(options.Value.DataRoot, "evals") })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                try
                {
                    var scenario = JsonSerializer.Deserialize<AgentEvalScenario>(File.ReadAllText(file), Json);
                    if (scenario is null || string.IsNullOrWhiteSpace(scenario.Id) || string.IsNullOrWhiteSpace(scenario.User))
                    {
                        logger.LogWarning("Eval scenario {File} has no id or user.", file);
                        continue;
                    }

                    found[scenario.Id] = scenario;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not read eval scenario {File}.", file);
                }
            }
        }

        return [.. found.Values.OrderBy(item => item.Id, StringComparer.Ordinal)];
    }

    public async Task<AgentEvalRunResult> RunAsync(string? id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var scenarios = Scenarios();
            if (id is { Length: > 0 })
            {
                scenarios = [.. scenarios.Where(item => item.Id == id)];
                if (scenarios.Count == 0)
                {
                    throw new AgentToolException($"No eval scenario '{id}'.");
                }
            }

            var cases = new List<AgentEvalCaseResult>();
            foreach (var scenario in scenarios)
            {
                cases.Add(await RunCaseAsync(scenario, cancellationToken));
            }

            var result = new AgentEvalRunResult(
                settings.Get().Model.Model,
                DateTimeOffset.UtcNow,
                cases.Count(item => item.Passed),
                cases.Count,
                cases);
            await SaveAsync(result, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentEvalRunResult?> LastAsync(CancellationToken cancellationToken) =>
        (await HistoryAsync(1, cancellationToken)).FirstOrDefault();

    public async Task<IReadOnlyList<AgentEvalRunResult>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(HistoryPath))
            {
                return [];
            }

            var history = JsonSerializer.Deserialize<AgentEvalHistoryDocument>(await File.ReadAllTextAsync(HistoryPath, cancellationToken), Json);
            return history?.Runs.Take(Math.Clamp(limit, 1, 50)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read the eval history.");
            return [];
        }
    }

    private async Task SaveAsync(AgentEvalRunResult result, CancellationToken cancellationToken)
    {
        try
        {
            var history = new AgentEvalHistoryDocument();
            if (File.Exists(HistoryPath))
            {
                try
                {
                    history = JsonSerializer.Deserialize<AgentEvalHistoryDocument>(await File.ReadAllTextAsync(HistoryPath, cancellationToken), Json)
                        ?? new AgentEvalHistoryDocument();
                }
                catch (JsonException)
                {
                    history = new AgentEvalHistoryDocument();
                }
            }

            history.Runs.Insert(0, result);
            history.Runs = [.. history.Runs.Take(20)];
            var temporary = HistoryPath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(history, Json), cancellationToken);
            File.Move(temporary, HistoryPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save the eval history.");
        }
    }

    private async Task<AgentEvalCaseResult> RunCaseAsync(AgentEvalScenario scenario, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var checks = new List<AgentEvalCheck>();
        var toolCalls = new List<string>();
        var promptTokens = 0;
        var completionTokens = 0;
        var answer = string.Empty;
        try
        {
            var evalTools = new List<AgentTool>();
            foreach (var (name, fake) in scenario.Tools)
            {
                var real = tools.Find(name);
                if (real is null)
                {
                    return Error(scenario, checks, toolCalls, $"The scenario names unknown tool '{name}'.", started);
                }

                evalTools.Add(name == "search_knowledge"
                    ? real
                    : real with { Handler = (_, _) => Task.FromResult<object>(fake.Clone()) });
            }

            var messages = new List<AgentChatMessage>
            {
                AgentChatMessage.System($"""
Bạn là GameWarden Operations Agent đang được đánh giá. Chỉ dùng các tool được cung cấp.
{scenario.Context}
Nếu một thao tác ghi cần approval, hãy nói rõ là cần duyệt; không được nói đã chạy xong.
Trả lời ngắn gọn, tiếng Việt, kết luận trước rồi bằng chứng.
"""),
                AgentChatMessage.User(scenario.User),
            };

            for (var step = 0; step < scenario.Expect.MaxToolCalls; step++)
            {
                var result = await model.CompleteAsync(messages, evalTools, cancellationToken);
                promptTokens += result.PromptTokens;
                completionTokens += result.CompletionTokens;
                if (result.ToolCalls.Count == 0)
                {
                    answer = result.Content ?? string.Empty;
                    break;
                }

                messages.Add(AgentChatMessage.Assistant(result.Content, result.ToolCalls));
                foreach (var call in result.ToolCalls)
                {
                    toolCalls.Add(call.Name);
                    var tool = evalTools.FirstOrDefault(item => item.Name == call.Name);
                    if (tool is null)
                    {
                        messages.Add(AgentChatMessage.Tool(call.Id, $"Unknown tool '{call.Name}'."));
                        continue;
                    }

                    using var args = JsonDocument.Parse(call.Arguments);
                    var output = await tools.ExecuteAsync(tool, args.RootElement, cancellationToken);
                    messages.Add(AgentChatMessage.Tool(call.Id, output));
                }
            }

            checks.Add(Check("answer not empty", !string.IsNullOrWhiteSpace(answer), answer));
            foreach (var expected in scenario.Expect.Tools)
            {
                checks.Add(Check($"used {expected}", toolCalls.Contains(expected, StringComparer.Ordinal), string.Join(", ", toolCalls)));
            }

            if (scenario.Expect.AnyTools.Count > 0)
            {
                checks.Add(Check(
                    $"used one of {string.Join("/", scenario.Expect.AnyTools)}",
                    scenario.Expect.AnyTools.Any(tool => toolCalls.Contains(tool, StringComparer.Ordinal)),
                    string.Join(", ", toolCalls)));
            }

            foreach (var forbidden in scenario.Expect.ForbiddenTools)
            {
                checks.Add(Check($"avoided {forbidden}", !toolCalls.Contains(forbidden, StringComparer.Ordinal), string.Join(", ", toolCalls)));
            }

            foreach (var text in scenario.Expect.AnswerContains)
            {
                checks.Add(Check($"answer contains '{text}'", answer.Contains(text, StringComparison.OrdinalIgnoreCase), answer));
            }

            if (scenario.Expect.AnswerContainsAny.Count > 0)
            {
                checks.Add(Check(
                    $"answer contains one of {string.Join("/", scenario.Expect.AnswerContainsAny)}",
                    scenario.Expect.AnswerContainsAny.Any(text => answer.Contains(text, StringComparison.OrdinalIgnoreCase)),
                    answer));
            }

            foreach (var text in scenario.Expect.AnswerNotContains)
            {
                checks.Add(Check($"answer avoids '{text}'", !answer.Contains(text, StringComparison.OrdinalIgnoreCase), answer));
            }

            checks.Add(Check(
                "tool budget",
                toolCalls.Count <= scenario.Expect.MaxToolCalls,
                $"{toolCalls.Count}/{scenario.Expect.MaxToolCalls}"));

            var passed = checks.All(item => item.Ok);
            var score = checks.Count == 0 ? 0 : (int)Math.Round(checks.Count(item => item.Ok) * 100.0 / checks.Count);
            return new AgentEvalCaseResult(
                scenario.Id,
                scenario.Name,
                passed,
                score,
                checks,
                toolCalls,
                Trim(answer),
                Elapsed(started),
                promptTokens,
                completionTokens,
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Eval scenario {Scenario} failed.", scenario.Id);
            return Error(scenario, checks, toolCalls, ex.Message, started);
        }
    }

    private static AgentEvalCaseResult Error(
        AgentEvalScenario scenario,
        IReadOnlyList<AgentEvalCheck> checks,
        IReadOnlyList<string> toolCalls,
        string error,
        long started) =>
        new(scenario.Id, scenario.Name, false, 0, checks, toolCalls, string.Empty, Elapsed(started), 0, 0, error);

    private static AgentEvalCheck Check(string name, bool ok, string? detail) => new(name, ok, detail);

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static string Trim(string value) => value.Length <= 2000 ? value : value[..2000] + "…";
}

/// <summary>Scenario label for the panel; the fake tool payload stays on the server.</summary>
public sealed record AgentEvalScenarioInfo(string Id, string Name);

public sealed class AgentEvalHistoryDocument
{
    public int Version { get; set; } = 1;

    public List<AgentEvalRunResult> Runs { get; set; } = [];
}
