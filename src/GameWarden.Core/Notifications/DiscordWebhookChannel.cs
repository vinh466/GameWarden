using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Observability;
using GameWarden.Core.Secrets;

namespace GameWarden.Core.Notifications;

public sealed record NotificationSendResult(
    bool Ok,
    string? MessageId = null,
    string? Error = null,
    bool Disable = false,
    bool Drop = false,
    TimeSpan? RetryAfter = null);

public interface INotificationChannel
{
    string Id { get; }

    Task<NotificationSendResult> SendAsync(
        NotificationDestination destination,
        NotificationEventRecord notification,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discord's incoming-webhook adapter (docs/18): one embed, severity colour, optional critical role mention, explicit
/// allowed mentions, Discord's own limits and rate-limit response. It knows nothing about GameWarden's features.
/// </summary>
public sealed partial class DiscordWebhookChannel(
    IHttpClientFactory httpClients,
    ISecretStore secrets,
    ILogger<DiscordWebhookChannel> logger) : INotificationChannel
{
    public const string HttpClientName = "notifications";
    public const string ChannelId = "discord-webhook";
    private const int EmbedLimit = 6000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Id => ChannelId;

    public async Task<NotificationSendResult> SendAsync(
        NotificationDestination destination,
        NotificationEventRecord notification,
        CancellationToken cancellationToken)
    {
        var url = secrets.Get(SecretScopes.Notifications, destination.Secret);
        if (string.IsNullOrWhiteSpace(url) || !NotificationSettings.IsWebhookUrl(url))
        {
            return new NotificationSendResult(false, Error: "The destination's webhook secret is not set or is not a Discord webhook URL.", Disable: true);
        }

        var payload = Payload(destination, notification);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, WaitUrl(url))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
        };
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GameWarden", CoreInfo.Version));
        using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return new NotificationSendResult(true, document.RootElement.GetProperty("id").GetString());
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return new NotificationSendResult(true);
            }
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = RetryAfter(response, text);
            return new NotificationSendResult(false, Error: "Discord rate limit.", RetryAfter: delay);
        }

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return new NotificationSendResult(false, Error: "Discord says this webhook no longer exists.", Disable: true);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new NotificationSendResult(false, Error: "Discord refused the webhook credentials.", Disable: true);
        }

        var detail = Clip(LogSanitizer.Clean(text), 300);
        logger.LogWarning("Discord webhook answered {Status}: {Body}", (int)response.StatusCode, detail);
        return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
            ? new NotificationSendResult(false, Error: $"Discord rejected the message: {detail}", Drop: true)
            : new NotificationSendResult(false, Error: $"Discord answered {(int)response.StatusCode}.");
    }

    private static object Payload(NotificationDestination destination, NotificationEventRecord notification)
    {
        var title = Clip(Clean(notification.Title), 256);
        var body = notification.Body is { Length: > 0 } ? Clip(Clean(notification.Body), 4096) : null;
        var footer = Clip($"GameWarden {CoreInfo.Version} · {notification.InstanceId ?? Environment.MachineName}", 2048);
        var used = title.Length + (body?.Length ?? 0) + footer.Length;
        var fields = Fields(notification.FieldsJson, ref used);
        var embed = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["timestamp"] = new DateTimeOffset(notification.At, TimeSpan.Zero).ToString("O"),
            ["color"] = Color(notification.Severity),
            ["footer"] = new { text = footer },
        };
        if (body is not null)
        {
            embed["description"] = body;
        }

        var link = notification.Url
            ?? (string.IsNullOrWhiteSpace(destination.PanelUrl)
                ? null
                : notification.InstanceId is { Length: > 0 } instance
                    ? $"{destination.PanelUrl.TrimEnd('/')}/servers/{Uri.EscapeDataString(instance)}"
                    : destination.PanelUrl);
        if (link is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            embed["url"] = url;
        }

        if (fields.Count > 0)
        {
            embed["fields"] = fields;
        }

        var payload = new Dictionary<string, object?>
        {
            ["username"] = "GameWarden",
            ["embeds"] = new[] { embed },
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
        };
        if (notification.Severity == "critical"
            && !string.IsNullOrWhiteSpace(destination.MentionRoleId)
            && destination.MentionRoleId.All(char.IsAsciiDigit))
        {
            payload["content"] = $"<@&{destination.MentionRoleId}>";
            payload["allowed_mentions"] = new { parse = new[] { "roles" }, roles = new[] { destination.MentionRoleId } };
        }

        return payload;
    }

    private static List<object> Fields(string? json, ref int used)
    {
        var fields = new List<object>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return fields;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var field in document.RootElement.EnumerateArray().Take(25))
            {
                var name = Clip(Clean(field.TryGetProperty("label", out var label) ? label.GetString() : "Field"), 256);
                var value = Clip(Clean(field.TryGetProperty("value", out var text) ? text.GetString() : "—"), 1024);
                if (used + name.Length + value.Length > EmbedLimit - 100)
                {
                    break;
                }

                used += name.Length + value.Length;
                fields.Add(new
                {
                    name,
                    value,
                    inline = field.TryGetProperty("inline", out var inline) && inline.GetBoolean(),
                });
            }
        }
        catch (JsonException)
        {
        }

        return fields;
    }

    private static int Color(string severity) => severity switch
    {
        "success" => 0x22C55E,
        "warning" => 0xF59E0B,
        "error" => 0xEF4444,
        "critical" => 0x991B1B,
        _ => 0x3B82F6,
    };

    private static string Clip(string? value, int length)
    {
        var text = value ?? string.Empty;
        if (text.Length <= length)
        {
            return text;
        }

        return length <= 1 ? text[..length] : text[..(length - 1)] + "…";
    }

    /// <summary>Game and user text must not be able to create a ping or a misleading link (docs/18 §5).</summary>
    private static string Clean(string? value)
    {
        var text = LogSanitizer.Clean(value);
        text = MentionPattern().Replace(text, "@…");
        text = MarkdownLinkPattern().Replace(text, "$1");
        return text;
    }

    private static string WaitUrl(string url) =>
        url.Contains('?', StringComparison.Ordinal) ? url + "&wait=true" : url + "?wait=true";

    private static TimeSpan RetryAfter(HttpResponseMessage response, string body)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("retry_after", out var value) && value.TryGetDouble(out var seconds))
            {
                return TimeSpan.FromSeconds(Math.Clamp(seconds, 0.1, 3600));
            }
        }
        catch (JsonException)
        {
        }

        return TimeSpan.FromSeconds(5);
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])@(everyone|here)\b|<@[!&]?\d+>|<#\d+>", RegexOptions.IgnoreCase)]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"\[([^\]]+)\]\((?!https?://)[^)]+\)")]
    private static partial Regex MarkdownLinkPattern();
}
