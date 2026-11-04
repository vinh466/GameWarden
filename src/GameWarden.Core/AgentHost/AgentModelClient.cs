using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameWarden.Core.AgentHost;

public sealed record AgentChatMessage(
    string Role,
    string? Content,
    IReadOnlyList<AgentToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public static AgentChatMessage System(string content) => new("system", content);
    public static AgentChatMessage User(string content) => new("user", content);
    public static AgentChatMessage Assistant(string? content, IReadOnlyList<AgentToolCall>? toolCalls = null) =>
        new("assistant", content, toolCalls);
    public static AgentChatMessage Tool(string toolCallId, string content) => new("tool", content, null, toolCallId);
}

public sealed record AgentToolCall(string Id, string Name, string Arguments);

public sealed record AgentModelResult(
    string? Content,
    IReadOnlyList<AgentToolCall> ToolCalls,
    int PromptTokens,
    int CompletionTokens);

public sealed class AgentModelException(string message) : Exception(message);

/// <summary>
/// OpenAI-compatible chat completions with tool calling (docs/20). The key comes from the secret store and is never
/// written to a prompt, a log or the database.
/// </summary>
public sealed class AgentModelClient(
    IHttpClientFactory httpClientFactory,
    AgentHostSettingsService settings,
    ILogger<AgentModelClient> logger)
{
    public const string HttpClientName = "agent-model";

    public async Task<AgentModelResult> CompleteAsync(
        IReadOnlyList<AgentChatMessage> messages,
        IReadOnlyList<AgentTool> tools,
        CancellationToken cancellationToken)
    {
        var model = settings.Get().Model;
        var key = settings.ModelApiKey();
        if (string.IsNullOrWhiteSpace(model.BaseUrl) || string.IsNullOrWhiteSpace(model.Model))
        {
            throw new AgentModelException("Model base URL and model id are not configured.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new AgentModelException("Model API key is not set (agent-host/model-api-key).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(model.TimeoutSeconds);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(model.BaseUrl))
        {
            Content = JsonContent.Create(new
            {
                model = model.Model,
                messages = messages.Select(MessageBody).ToArray(),
                tools = tools.Select(tool => new
                {
                    type = "function",
                    function = new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        parameters = tool.Parameters,
                    },
                }).ToArray(),
                tool_choice = "auto",
                temperature = model.Temperature,
                max_tokens = model.MaxTokens,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Model endpoint answered {Status}: {Body}", (int)response.StatusCode, Trim(body));
            throw new AgentModelException($"Model endpoint answered HTTP {(int)response.StatusCode}.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var choice = root.GetProperty("choices")[0].GetProperty("message");
            var content = choice.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
            var calls = new List<AgentToolCall>();
            if (choice.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in toolCalls.EnumerateArray())
                {
                    var function = call.GetProperty("function");
                    calls.Add(new AgentToolCall(
                        call.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                        function.GetProperty("name").GetString() ?? string.Empty,
                        function.GetProperty("arguments").GetString() ?? "{}"));
                }
            }

            var promptTokens = root.TryGetProperty("usage", out var usage)
                && usage.TryGetProperty("prompt_tokens", out var prompt) ? prompt.GetInt32() : 0;
            var completionTokens = usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("completion_tokens", out var completion) ? completion.GetInt32() : 0;
            return new AgentModelResult(content, calls, promptTokens, completionTokens);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Model endpoint returned an unreadable body: {Body}", Trim(body));
            throw new AgentModelException("Model endpoint returned an unreadable response.");
        }
    }

    private static object MessageBody(AgentChatMessage message)
    {
        if (message.Role == "tool")
        {
            return new { role = "tool", tool_call_id = message.ToolCallId, content = message.Content ?? string.Empty };
        }

        if (message.ToolCalls is { Count: > 0 } calls)
        {
            return new
            {
                role = "assistant",
                content = message.Content,
                tool_calls = calls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new { name = call.Name, arguments = call.Arguments },
                }).ToArray(),
            };
        }

        return new { role = message.Role, content = message.Content ?? string.Empty };
    }

    private static string Endpoint(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/chat/completions";
    }

    private static string Trim(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
