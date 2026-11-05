using System.Net;
using System.Text;
using GameWarden.Core.AgentHost;
using GameWarden.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class AgentHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gamewarden-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Settings_validate_channels_and_secrets_stay_write_only()
    {
        var secrets = new FakeSecrets();
        var settings = new AgentHostSettingsService(
            Options.Create(new GameWardenOptions { DataRoot = _root }),
            secrets,
            NullLogger<AgentHostSettingsService>.Instance);

        settings.Update(new AgentHostSettingsDocument
        {
            Enabled = true,
            Model = new AgentModelSettings
            {
                BaseUrl = "https://model.test/v1",
                Model = "deepseek-v4.1-flash",
                Headers = new Dictionary<string, string>
                {
                    ["X-Test"] = "yes",
                    ["X-Key"] = "%apiKey%",
                },
            },
            Discord = new AgentDiscordSettings
            {
                GuildId = "42",
                Channels = new Dictionary<string, string> { ["valheim-main"] = "99", ["system"] = "100" },
            },
        });
        settings.SetSecret(AgentHostSettingsService.ModelApiKeySecret, "secret-key");

        Assert.Equal("secret-key", settings.ModelApiKey());
        Assert.Equal("valheim-main", settings.ChannelTarget(99));
        Assert.Equal("system", settings.ChannelTarget(100));
        Assert.True(settings.View().ModelConfigured);
        Assert.True(settings.View().ModelKeySet);
        Assert.False(settings.View().BotTokenSet);
        Assert.DoesNotContain("secret-key", File.ReadAllText(settings.PathOf()), StringComparison.Ordinal);

        Assert.Throws<AgentHostException>(() => settings.Update(new AgentHostSettingsDocument
        {
            Discord = new AgentDiscordSettings { Language = "fr" },
        }));
    }

    [Fact]
    public async Task Model_client_sends_openai_tools_and_reads_tool_calls()
    {
        var secrets = new FakeSecrets();
        var settings = new AgentHostSettingsService(
            Options.Create(new GameWardenOptions { DataRoot = _root }),
            secrets,
            NullLogger<AgentHostSettingsService>.Instance);
        settings.Update(new AgentHostSettingsDocument
        {
            Model = new AgentModelSettings
            {
                BaseUrl = "https://model.test/v1",
                Model = "deepseek-v4.1-flash",
                Headers = new Dictionary<string, string>
                {
                    ["X-Test"] = "yes",
                    ["X-Key"] = "%apiKey%",
                },
            },
        });
        settings.SetSecret(AgentHostSettingsService.ModelApiKeySecret, "test-key");

        const string body = """
            {
              "choices": [{ "message": {
                "content": "Đã kiểm tra.",
                "tool_calls": [{
                  "id": "call-1",
                  "type": "function",
                  "function": { "name": "status", "arguments": "{}" }
                }]
              }}],
              "usage": { "prompt_tokens": 11, "completion_tokens": 7 }
            }
            """;
        var handler = new Handler(body);
        var client = new AgentModelClient(
            new Factory(new HttpClient(handler)),
            settings,
            NullLogger<AgentModelClient>.Instance);

        var result = await client.CompleteAsync([AgentChatMessage.User("kiểm tra")], [], TestContext.Current.CancellationToken);

        Assert.Equal("Đã kiểm tra.", result.Content);
        Assert.Equal(("call-1", "status", "{}"), (result.ToolCalls[0].Id, result.ToolCalls[0].Name, result.ToolCalls[0].Arguments));
        Assert.Equal((11, 7), (result.PromptTokens, result.CompletionTokens));
        Assert.Contains("Bearer", handler.Authorization, StringComparison.Ordinal);
        Assert.Equal("yes", handler.Headers["X-Test"]);
        Assert.Equal("test-key", handler.Headers["X-Key"]);
        Assert.Contains("\"model\":\"deepseek-v4.1-flash\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"tools\":[]", handler.Body, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        public string Authorization { get; private set; } = string.Empty;

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = string.Join(",", header.Value);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeSecrets : ISecretStore
    {
        private readonly Dictionary<(string Scope, string Name), string> _values = [];

        public void Set(string scope, string name, string value) => _values[(scope, name)] = value;

        public string? Get(string scope, string name) =>
            _values.GetValueOrDefault((scope, name));

        public IReadOnlyList<string> Names(string scope) =>
            _values.Keys.Where(key => key.Scope == scope).Select(key => key.Name).ToArray();

        public bool Delete(string scope, string name) => _values.Remove((scope, name));

        public void DeleteScope(string scope)
        {
            foreach (var key in _values.Keys.Where(key => key.Scope == scope).ToArray())
            {
                _values.Remove(key);
            }
        }
    }
}
