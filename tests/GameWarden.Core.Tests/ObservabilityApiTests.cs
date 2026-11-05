using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameWarden.Core.Data;
using GameWarden.Core.Notifications;
using GameWarden.Core.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GameWarden.Core.Tests;

/// <summary>The queryable system log and the Discord notification outbox (docs/18, docs/19).</summary>
[Trait("Category", "Api")]
[Collection(CoreApiFactory.Collection)]
public sealed class ObservabilityApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Core_logs_reach_the_system_log_redacted_and_filterable()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);
        var marker = "observability-" + Guid.NewGuid().ToString("N")[..8];
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GameWarden.Core.Tests.Observability");

        logger.LogInformation(
            "Probe {Marker} password=super-secret-{Marker} webhook https://discord.com/api/webhooks/123456/abcdef.",
            marker,
            marker);
        logger.LogWarning("Probe {Marker} warning.", marker);

        var page = await WaitForLogAsync(cli, $"/api/v1/system/logs?q={marker}&limit=10");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        var info = items.Single(item => item.GetProperty("level").GetString() == "info");
        var warn = items.Single(item => item.GetProperty("level").GetString() == "warn");
        var text = info.GetProperty("message").GetString()!;

        Assert.Equal("GameWarden.Core.Tests.Observability", info.GetProperty("component").GetString());
        Assert.DoesNotContain("super-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef", text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
        Assert.Equal("Probe " + marker + " warning.", warn.GetProperty("message").GetString());

        var warningFilter = await WaitForLogAsync(cli, $"/api/v1/system/logs?level=warning&q={marker}");
        Assert.All(warningFilter.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("warn", item.GetProperty("level").GetString()));

        var future = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system/logs?from=2999-01-01T00:00:00Z", Ct);
        Assert.Empty(future.GetProperty("items").EnumerateArray());

        var summary = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system/logs/summary?minutes=60", Ct);
        Assert.True(summary.GetProperty("total").GetInt32() > 0);
        Assert.Contains(summary.GetProperty("levels").EnumerateArray(),
            level => level.GetProperty("key").GetString() == "warn");
        Assert.Contains(summary.GetProperty("components").EnumerateArray(),
            component => component.GetProperty("key").GetString() == "GameWarden.Core.Tests.Observability");

        var components = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system/logs/components", Ct);
        var component = components.EnumerateArray()
            .Single(row => row.GetProperty("component").GetString() == "GameWarden.Core.Tests.Observability");
        Assert.True(component.GetProperty("count").GetInt32() >= 2);
        Assert.NotEqual(JsonValueKind.Null, component.GetProperty("lastAt").ValueKind);
    }

    [Fact]
    public async Task Log_scopes_and_instance_categories_fill_the_query_filters()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);
        var marker = "scope-" + Guid.NewGuid().ToString("N")[..8];
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GameWarden.Core.Tests.Scoped");
        using (logger.BeginScope(new Dictionary<string, object?>
               {
                   ["instanceId"] = marker,
                   ["jobId"] = marker + "-job",
                   ["requestId"] = marker + "-request",
               }))
        {
            logger.LogInformation("Scoped probe {Marker}.", marker);
        }

        factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GameWarden.Instance." + marker)
            .LogInformation("Category-only instance probe.");

        var scoped = await WaitForLogAsync(cli,
            $"/api/v1/system/logs?q={marker}&instanceId={marker}&jobId={marker}-job&requestId={marker}-request");
        var row = scoped.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(marker, row.GetProperty("instanceId").GetString());
        Assert.Equal(marker + "-job", row.GetProperty("jobId").GetString());
        Assert.Equal(marker + "-request", row.GetProperty("requestId").GetString());

        var instanceOnly = await WaitForLogAsync(cli, $"/api/v1/system/logs?instanceId={marker}&limit=20");
        Assert.Contains(instanceOnly.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("component").GetString() == "GameWarden.Instance." + marker
                && item.GetProperty("instanceId").GetString() == marker);
    }

    [Fact]
    public async Task The_agent_sees_the_same_system_log_only_inside_diagnostics_mode()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);
        var marker = "agent-log-" + Guid.NewGuid().ToString("N")[..8];
        factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GameWarden.Core.Tests.AgentObservability")
            .LogError("Probe {Marker} failed.", marker);
        _ = await WaitForLogAsync(cli, $"/api/v1/system/logs?q={marker}");

        var (_, token) = await CreateAgentAsync(factory, ["agent.logs.read"]);
        using var agent = AgentClient(factory, token);

        var before = await agent.GetAsync($"/api/v1/agent/logs?source=system&q={marker}", Ct);
        var summaryBefore = await agent.GetAsync("/api/v1/agent/logs/summary?minutes=60", Ct);
        await cli.PutAsJsonAsync(
            "/api/v1/agents/settings",
            new { diagnostics = new { enabled = true, ttlMinutes = 30, reason = "observability test" } },
            Ct);
        var after = await agent.GetAsync($"/api/v1/agent/logs?source=system&q={marker}", Ct);
        var summaryAfter = await agent.GetAsync("/api/v1/agent/logs/summary?minutes=60", Ct);

        await AssertProblemAsync(before, HttpStatusCode.Forbidden, "diagnostics_mode_off");
        await AssertProblemAsync(summaryBefore, HttpStatusCode.Forbidden, "diagnostics_mode_off");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(HttpStatusCode.OK, summaryAfter.StatusCode);

        var view = await after.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("system", view.GetProperty("source").GetString());
        Assert.Equal("system_log", view.GetProperty("file").GetString());
        Assert.True(view.GetProperty("redacted").GetBoolean());
        var line = view.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("error", line.GetProperty("level").GetString());
        Assert.Equal("GameWarden.Core.Tests.AgentObservability", line.GetProperty("component").GetString());
        Assert.Contains(marker, line.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.True((await summaryAfter.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("total").GetInt32() > 0);
    }

    [Fact]
    public async Task Notification_settings_hide_the_webhook_and_accept_supported_modes()
    {
        using var factory = new CoreApiFactory();
        var cli = Cli(factory);
        var webhook = "https://discord.com/api/webhooks/123456789/token-value";

        var empty = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system/notifications", Ct);
        var digest = await cli.PutAsJsonAsync("/api/v1/system/notifications", new
        {
            destinations = new[] { Destination("alerts", "Alerts", "digest", webhook) },
        }, Ct);
        var badUrl = await cli.PutAsJsonAsync("/api/v1/system/notifications", new
        {
            destinations = new[] { Destination("alerts", "Alerts", "instant", "https://example.com/hook") },
        }, Ct);
        var saved = await cli.PutAsJsonAsync("/api/v1/system/notifications", new
        {
            destinations = new[] { Destination("alerts", "Alerts", "instant", webhook) },
        }, Ct);

        Assert.Empty(empty.GetProperty("destinations").EnumerateArray());
        Assert.Equal(
            ["instant", "digest", "status"],
            empty.GetProperty("supportedModes").EnumerateArray().Select(mode => mode.GetString()));
        Assert.Equal(HttpStatusCode.OK, digest.StatusCode);
        await AssertProblemAsync(badUrl, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var view = await saved.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var destination = view.GetProperty("destinations").EnumerateArray().Single();
        Assert.True(destination.GetProperty("webhookSet").GetBoolean());
        Assert.DoesNotContain("token-value", view.GetRawText(), StringComparison.Ordinal);

        var json = await File.ReadAllTextAsync(Path.Combine(factory.DataRoot, NotificationSettings.FileName), Ct);
        Assert.DoesNotContain("token-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("discord.com", json, StringComparison.Ordinal);
        var secrets = factory.Services.GetRequiredService<ISecretStore>();
        Assert.Equal(webhook, secrets.Get(SecretScopes.Notifications, "alerts"));
    }

    [Fact]
    public async Task Agent_host_settings_keep_the_model_key_and_bot_token_out_of_the_body()
    {
        using var factory = new CoreApiFactory();
        using var cli = Cli(factory);

        var saved = await cli.PutAsJsonAsync("/api/v1/system/agent-host", new
        {
            schemaVersion = 1,
            enabled = true,
            model = new
            {
                baseUrl = "https://model.test/v1",
                model = "deepseek-v4.1-flash",
                maxTokens = 4096,
                temperature = 0.2,
                maxToolCalls = 12,
                timeoutSeconds = 120,
            },
            discord = new
            {
                guildId = "42",
                channels = new Dictionary<string, string> { ["valheim-main"] = "99", ["system"] = "100" },
                allowedUserIds = Array.Empty<ulong>(),
                allowedRoleIds = Array.Empty<ulong>(),
                requireMention = true,
                language = "vi",
            },
            autonomy = new
            {
                mode = "approve-writes",
                approvalTimeoutMinutes = 15,
                maxWritesPerHour = 6,
            },
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var view = await saved.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(view.GetProperty("settings").GetProperty("enabled").GetBoolean());
        Assert.Equal("valheim-main", view.GetProperty("settings").GetProperty("discord").GetProperty("channels").EnumerateObject().First().Name);

        var secret = await cli.PutAsJsonAsync("/api/v1/system/agent-host/secrets/model-api-key", new { value = "sk-test-value" }, Ct);
        Assert.Equal(HttpStatusCode.OK, secret.StatusCode);
        var after = await secret.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(after.GetProperty("modelKeySet").GetBoolean());
        Assert.DoesNotContain("sk-test-value", after.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_preview_returns_the_live_status_message()
    {
        using var factory = new CoreApiFactory();
        using var cli = Cli(factory);

        var response = await cli.GetAsync("/api/v1/system/notifications/status-preview", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task A_published_event_becomes_a_durable_delivery_and_a_test_reaches_the_channel()
    {
        var channel = new FakeChannel();
        using var factory = new FakeChannelFactory(channel);
        var cli = Cli(factory);
        var saved = await cli.PutAsJsonAsync("/api/v1/system/notifications", new
        {
            destinations = new[]
            {
                Destination("alerts", "Alerts", "instant", "https://discord.com/api/webhooks/123456789/token-value"),
            },
        }, Ct);
        saved.EnsureSuccessStatusCode();

        var test = await cli.PostAsJsonAsync("/api/v1/system/notifications/destinations/alerts/test", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.True(channel.Sent.TryPeek(out var tested));
        Assert.Equal("system.test", tested!.Topic);

        var notifications = factory.Services.GetRequiredService<NotificationService>();
        notifications.Publish(
            "instance.crashed",
            "error",
            "valheim-main crashed",
            instanceId: "valheim-main",
            dedupeKey: "crash-test");
        notifications.Publish(
            "instance.crashed",
            "error",
            "valheim-main crashed",
            instanceId: "valheim-main",
            dedupeKey: "crash-test");

        var delivery = await WaitForDeliveryAsync(cli, "instance.crashed");
        Assert.Equal("alerts", delivery.GetProperty("destinationId").GetString());
        Assert.Equal("sent", delivery.GetProperty("state").GetString());
        Assert.StartsWith("fake-", delivery.GetProperty("messageId").GetString(), StringComparison.Ordinal);

        var events = await factory.Services.GetRequiredService<IDbContextFactory<GameWardenDb>>()
            .CreateDbContextAsync(Ct);
        await using (events)
        {
            Assert.Equal(1, events.NotificationEvents.Count(row => row.DedupeKey == "crash-test"));
        }

        Assert.Equal(1, channel.Sent.Count(notification => notification.Topic == "instance.crashed"));
    }

    [Fact]
    public async Task A_status_destination_posts_one_message_and_keeps_its_id()
    {
        var channel = new FakeChannel();
        using var factory = new FakeChannelFactory(channel);
        var cli = Cli(factory);
        var saved = await cli.PutAsJsonAsync("/api/v1/system/notifications", new
        {
            destinations = new[]
            {
                Destination("status", "Status", "status", "https://discord.com/api/webhooks/123456789/token-value"),
            },
        }, Ct);
        saved.EnsureSuccessStatusCode();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!channel.Sent.Any(notification => notification.Topic == "status") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, Ct);
        }

        Assert.Contains(channel.Sent, notification => notification.Topic == "status");

        NotificationStatusRecord? row = null;
        var rowDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<GameWardenDb>>()
            .CreateDbContextAsync(Ct);
        while (row is null && DateTime.UtcNow < rowDeadline)
        {
            row = await db.NotificationStatuses.AsNoTracking()
                .SingleOrDefaultAsync(status => status.DestinationId == "status", Ct);
            if (row is null)
            {
                await Task.Delay(100, Ct);
            }
        }

        Assert.NotNull(row);
        Assert.StartsWith("fake-", row.MessageId, StringComparison.Ordinal);
        Assert.Contains("No servers configured", row.Content, StringComparison.Ordinal);
    }

    private static object Destination(string id, string name, string mode, string webhook) => new
    {
        id,
        name,
        channel = "discord-webhook",
        enabled = true,
        mode,
        language = "vi",
        minimumSeverity = "info",
        topics = new[] { "*" },
        instances = new[] { "*" },
        mentionRoleId = (string?)null,
        panelUrl = (string?)null,
        intervalSec = 60,
        digestSec = 300,
        webhookUrl = webhook,
    };

    private static async Task<JsonElement> WaitForLogAsync(HttpClient cli, string url)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        do
        {
            var response = await cli.GetAsync(url, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            if (response.IsSuccessStatusCode)
            {
                var page = JsonDocument.Parse(text).RootElement;
                if (page.GetProperty("items").GetArrayLength() > 0)
                {
                    return page;
                }
            }
            else
            {
                throw new InvalidOperationException($"{url} answered {(int)response.StatusCode}: {text}");
            }

            await Task.Delay(100, Ct);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"No system log rows for {url}.");
    }

    private static async Task<JsonElement> WaitForDeliveryAsync(HttpClient cli, string topic)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        do
        {
            var rows = await cli.GetFromJsonAsync<JsonElement>("/api/v1/system/notifications/deliveries?limit=50", Ct);
            var row = rows.EnumerateArray()
                .FirstOrDefault(item => item.GetProperty("topic").GetString() == topic);
            if (row.ValueKind != JsonValueKind.Undefined && row.GetProperty("state").GetString() is "sent" or "failed" or "dropped")
            {
                return row;
            }

            await Task.Delay(200, Ct);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"No delivery for {topic}.");
    }

    private static HttpClient Cli(CoreApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CliToken());
        return client;
    }

    private static HttpClient AgentClient(CoreApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("gamewarden-test/1.0");
        return client;
    }

    private static async Task<(string ClientId, string Token)> CreateAgentAsync(
        CoreApiFactory factory,
        IReadOnlyList<string> permissions)
    {
        var cli = Cli(factory);
        (await cli.PutAsJsonAsync("/api/v1/agents/settings", new { enabled = true }, Ct)).EnsureSuccessStatusCode();
        var created = await cli.PostAsJsonAsync(
            "/api/v1/agents",
            new { name = "observability-agent", permissions, allowedNetworks = Array.Empty<string>() },
            Ct);
        created.EnsureSuccessStatusCode();
        var clientId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        var tokenResponse = await cli.PostAsJsonAsync(
            $"/api/v1/agents/{clientId}/tokens",
            new { name = "test", expiresInDays = 7 },
            Ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
        return (clientId, token);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private sealed class FakeChannelFactory(FakeChannel channel) : CoreApiFactory
    {
        protected override void ConfigureTestServices(IServiceCollection services)
        {
            services.RemoveAll<INotificationChannel>();
            services.AddSingleton<INotificationChannel>(channel);
        }
    }

    private sealed class FakeChannel : INotificationChannel
    {
        public ConcurrentQueue<NotificationEventRecord> Sent { get; } = new();

        public string Id => DiscordWebhookChannel.ChannelId;

        public Task<NotificationSendResult> SendAsync(
            NotificationDestination destination,
            NotificationEventRecord notification,
            CancellationToken cancellationToken)
        {
            Sent.Enqueue(notification);
            return Task.FromResult(new NotificationSendResult(true, $"fake-{Sent.Count}"));
        }

        public Task<NotificationSendResult> UpdateAsync(
            NotificationDestination destination,
            string messageId,
            NotificationEventRecord notification,
            CancellationToken cancellationToken)
        {
            Sent.Enqueue(notification);
            return Task.FromResult(new NotificationSendResult(true, messageId));
        }
    }
}
