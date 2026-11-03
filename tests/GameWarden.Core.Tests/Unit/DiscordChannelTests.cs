using System.Net;
using System.Text;
using System.Text.Json;
using GameWarden.Core.Data;
using GameWarden.Core.Notifications;
using GameWarden.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class DiscordChannelTests
{
    private const string Webhook = "https://discord.com/api/webhooks/123456789/token-value";

    [Fact]
    public async Task The_payload_fits_Discord_limits_and_cannot_ping_everyone()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"id\":\"message-1\"}");
        var channel = Channel(handler, Webhook);
        var notification = Event(
            "@everyone crashed <@&123456789012345678>",
            new string('x', 5000),
            JsonSerializer.Serialize(Enumerable.Range(0, 30)
                .Select(index => new { label = $"Field {index}", value = new string('y', 1024) })));

        var result = await channel.SendAsync(Destination(), notification, CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("message-1", result.MessageId);
        Assert.Contains("wait=true", handler.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Equal("GameWarden", handler.Request!.Headers.UserAgent.ToString().Split('/')[0]);
        using var payload = JsonDocument.Parse(handler.Body!);
        var embed = payload.RootElement.GetProperty("embeds")[0];
        Assert.DoesNotContain("@everyone", embed.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("<@&", embed.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.True(embed.GetProperty("description").GetString()!.Length <= 4096);
        Assert.True(embed.GetProperty("fields").GetArrayLength() <= 25);
        var total = embed.GetProperty("title").GetString()!.Length
            + embed.GetProperty("description").GetString()!.Length
            + embed.GetProperty("footer").GetProperty("text").GetString()!.Length
            + embed.GetProperty("fields").EnumerateArray()
                .Sum(field => field.GetProperty("name").GetString()!.Length + field.GetProperty("value").GetString()!.Length);
        Assert.True(total <= 6000, $"embed uses {total} characters");
        Assert.Empty(payload.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
    }

    [Fact]
    public async Task Only_a_critical_event_mentions_the_configured_role()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"id\":\"message-2\"}");
        var channel = Channel(handler, Webhook + "?thread_id=42");
        var destination = Destination() with { MentionRoleId = "123456789012345678" };

        await channel.SendAsync(destination, Event("critical", null, null), CancellationToken.None);
        await channel.SendAsync(destination, Event("warning", null, null), CancellationToken.None);

        Assert.Contains("&wait=true", handler.RequestUris[0].Query, StringComparison.Ordinal);
        using var critical = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("<@&123456789012345678>", critical.RootElement.GetProperty("content").GetString());
        Assert.Equal("roles", critical.RootElement.GetProperty("allowed_mentions").GetProperty("parse")[0].GetString());
        using var warning = JsonDocument.Parse(handler.Bodies[1]!);
        Assert.False(warning.RootElement.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task A_status_message_is_edited_in_place_and_recreated_when_deleted()
    {
        var edited = new FakeHandler(HttpStatusCode.OK, "{\"id\":\"message-99\"}");
        var channel = Channel(edited, Webhook);

        var result = await channel.UpdateAsync(Destination(), "message-99", Event("status", "two servers", null), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Patch, edited.Request!.Method);
        Assert.Contains("/messages/message-99", edited.RequestUri!.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("wait=true", edited.RequestUri.Query, StringComparison.Ordinal);

        var deleted = new FakeHandler(HttpStatusCode.NotFound, "{}");
        var missing = await Channel(deleted, Webhook)
            .UpdateAsync(Destination(), "message-99", Event("status", null, null), CancellationToken.None);

        Assert.True(missing.Missing);
        Assert.False(missing.Disable);
    }

    [Fact]
    public async Task A_rate_limit_is_returned_as_a_delay()
    {
        var handler = new FakeHandler(HttpStatusCode.TooManyRequests, "{\"retry_after\":2.5}", response =>
            response.Headers.Add("Retry-After", "2"));
        var channel = Channel(handler, Webhook);

        var result = await channel.SendAsync(Destination(), Event("crashed", null, null), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(TimeSpan.FromSeconds(2), result.RetryAfter);
        Assert.False(result.Disable);
    }

    [Fact]
    public async Task A_deleted_webhook_disables_the_destination_and_a_rejected_message_is_dropped()
    {
        var deleted = new FakeHandler(HttpStatusCode.NotFound, "{}");
        var rejected = new FakeHandler(HttpStatusCode.BadRequest, "{\"message\":\"bad embed\"}");

        var missing = await Channel(deleted, Webhook).SendAsync(Destination(), Event("crashed", null, null), CancellationToken.None);
        var bad = await Channel(rejected, Webhook).SendAsync(Destination(), Event("crashed", null, null), CancellationToken.None);

        Assert.True(missing.Disable);
        Assert.True(bad.Drop);
        Assert.Contains("bad embed", bad.Error, StringComparison.Ordinal);
    }

    private static DiscordWebhookChannel Channel(FakeHandler handler, string url) =>
        new(new FakeClients(handler), new FakeSecrets(url), NullLogger<DiscordWebhookChannel>.Instance);

    private static NotificationDestination Destination() => new(
        "alerts", "Alerts", DiscordWebhookChannel.ChannelId, "alerts", MinimumSeverity: "info");

    private static NotificationEventRecord Event(string title, string? body, string? fields) => new()
    {
        At = DateTime.UtcNow,
        Topic = "instance.crashed",
        Severity = title == "critical" ? "critical" : "error",
        Title = title,
        Body = body,
        FieldsJson = fields,
        Source = "core",
    };

    private sealed class FakeClients(FakeHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeHandler(
        HttpStatusCode status,
        string responseBody,
        Action<HttpResponseMessage>? configure = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public Uri? RequestUri => Request?.RequestUri;

        public List<Uri> RequestUris { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestUris.Add(request.RequestUri!);
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(Body);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
            configure?.Invoke(response);
            return response;
        }
    }

    private sealed class FakeSecrets(string url) : ISecretStore
    {
        public void Set(string scope, string name, string value)
        {
        }

        public string? Get(string scope, string name) =>
            scope == SecretScopes.Notifications && name == "alerts" ? url : null;

        public IReadOnlyList<string> Names(string scope) => [];

        public bool Delete(string scope, string name) => false;

        public void DeleteScope(string scope)
        {
        }
    }
}
