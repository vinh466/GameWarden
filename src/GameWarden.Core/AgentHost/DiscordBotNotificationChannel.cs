using System.Text.Json;
using Discord;
using GameWarden.Core.Data;
using GameWarden.Core.Notifications;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// Discord notifications sent by the bot instead of a webhook (docs/21 §4.4). The destination is routed by
/// <c>instanceId</c> to the channel already mapped for that server, or to the system channel. The webhook channel
/// remains the fallback when the bot is off.
/// </summary>
public sealed class DiscordBotNotificationChannel(
    DiscordClientProvider clients,
    AgentHostSettingsService settings,
    ILogger<DiscordBotNotificationChannel> logger) : INotificationChannel
{
    public const string ChannelId = "discord-bot";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Id => ChannelId;

    public async Task<NotificationSendResult> SendAsync(
        NotificationDestination destination,
        NotificationEventRecord notification,
        CancellationToken cancellationToken)
    {
        if (!clients.Connected)
        {
            return new NotificationSendResult(false, Error: "The Discord bot is not connected; the delivery will retry.");
        }

        if (Channel(notification.InstanceId) is not { } channel)
        {
            return new NotificationSendResult(false, Error: "No Discord channel is mapped for this destination yet.", Drop: true);
        }

        try
        {
            var message = await channel.SendMessageAsync(Render(notification));
            return new NotificationSendResult(true, message.Id.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Discord bot could not send notification {Topic}.", notification.Topic);
            return new NotificationSendResult(false, Error: ex.Message);
        }
    }

    public async Task<NotificationSendResult> UpdateAsync(
        NotificationDestination destination,
        string messageId,
        NotificationEventRecord notification,
        CancellationToken cancellationToken)
    {
        if (!clients.Connected)
        {
            return new NotificationSendResult(false, Error: "The Discord bot is not connected; the delivery will retry.");
        }

        if (Channel(notification.InstanceId) is not { } channel)
        {
            return new NotificationSendResult(false, Error: "No Discord channel is mapped for this destination yet.", Drop: true);
        }

        if (!ulong.TryParse(messageId, out var id))
        {
            return new NotificationSendResult(false, Error: "The stored status message id is invalid.", Missing: true);
        }

        try
        {
            await channel.ModifyMessageAsync(id, message => message.Content = Render(notification));
            return new NotificationSendResult(true, messageId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Discord bot could not update notification {Topic}.", notification.Topic);
            return new NotificationSendResult(false, Error: ex.Message, Missing: true);
        }
    }

    private IMessageChannel? Channel(string? instanceId)
    {
        var client = clients.Client;
        var discord = settings.Get().Discord;
        if (client is null)
        {
            return null;
        }

        var key = instanceId is { Length: > 0 } && discord.Channels.ContainsKey(instanceId) ? instanceId : "system";
        return discord.Channels.TryGetValue(key, out var raw)
            && ulong.TryParse(raw, out var id)
            ? client.GetChannel(id) as IMessageChannel
            : null;
    }

    private static string Render(NotificationEventRecord notification)
    {
        var server = notification.InstanceId is { Length: > 0 } ? $" · {notification.InstanceId}" : string.Empty;
        var text = $"**{notification.Title}**{server}";
        if (!string.IsNullOrWhiteSpace(notification.Body))
        {
            text += $"\n{notification.Body}";
        }

        if (notification.FieldsJson is { Length: > 0 })
        {
            try
            {
                var fields = JsonSerializer.Deserialize<List<NotificationField>>(notification.FieldsJson, Json) ?? [];
                foreach (var field in fields)
                {
                    text += $"\n**{field.Label}:** {field.Value}";
                }
            }
            catch (JsonException)
            {
            }
        }

        if (notification.Url is { Length: > 0 } url)
        {
            text += $"\n{url}";
        }

        return text.Length <= 1900 ? text : text[..1900] + "…";
    }
}
