using Discord;
using Discord.WebSocket;
using GameWarden.Core.Data;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// Discord implementation of <see cref="IAgentMessenger"/> (docs/21 §2.1). It posts incident cards, creates one thread
/// per incident and posts approval cards. It never runs a capability; buttons are handled by the gateway bot.
/// </summary>
public sealed class DiscordMessenger(
    DiscordClientProvider clients,
    AgentHostSettingsService settings,
    ILogger<DiscordMessenger> logger) : IAgentMessenger
{
    public bool Connected => clients.Connected;

    public async Task<AgentMessageRef?> PostIncidentAsync(
        AgentIncidentRecord incident,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (Channel(incident.InstanceId) is not { } channel)
        {
            return null;
        }

        var vietnamese = settings.Get().Discord.Language == "vi";
        var title = vietnamese ? "Sự cố mới" : "New incident";
        var server = displayName ?? incident.InstanceId ?? "system";
        var text = Truncate($"""
**{title} · {server}**
`{incident.Severity}` · {incident.Title}
{incident.Summary ?? ""}
{(vietnamese ? "Mở lúc" : "Opened")} {incident.OpenedAt:HH:mm} UTC
""");
        var components = new ComponentBuilder()
            .WithButton(vietnamese ? "Chẩn đoán" : "Diagnose", $"agent-incident:diagnose:{incident.Id}", ButtonStyle.Primary)
            .WithButton(vietnamese ? "Restart" : "Restart", $"agent-incident:restart:{incident.Id}", ButtonStyle.Danger)
            .WithButton(vietnamese ? "Im lặng" : "Silence", $"agent-incident:silence:{incident.Id}", ButtonStyle.Secondary)
            .Build();
        var message = await channel.SendMessageAsync(text, components: components);
        ulong? threadId = null;
        if (channel is SocketTextChannel textChannel)
        {
            try
            {
                var thread = await textChannel.CreateThreadAsync(
                    $"incident-{incident.OpenedAt:yyyyMMdd-HHmm}",
                    ThreadType.PublicThread,
                    ThreadArchiveDuration.OneDay,
                    message);
                threadId = thread.Id;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not create a thread for incident {Incident}.", incident.Id);
            }
        }

        return new AgentMessageRef(channel.Id, message.Id, threadId);
    }

    public async Task PostIncidentNoteAsync(AgentIncidentRecord incident, string text, CancellationToken cancellationToken)
    {
        if (ChannelFor(incident) is not { } channel)
        {
            return;
        }

        await channel.SendMessageAsync(Truncate(text));
    }

    public async Task<AgentMessageRef?> PostApprovalAsync(
        AgentApprovalRecord approval,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (ChannelForApproval(approval) is not { } channel)
        {
            return null;
        }

        var vietnamese = settings.Get().Discord.Language == "vi";
        var text = Truncate($"""
**{(vietnamese ? "Cần duyệt" : "Approval needed")}:** `{approval.Tool}`
{(vietnamese ? "Server" : "Server")}: {displayName ?? approval.InstanceId ?? "system"}
{approval.Impact ?? ""}
{(vietnamese ? "Hết hạn" : "Expires")}: {approval.ExpiresAt.ToLocalTime():HH:mm} ({settings.Get().Autonomy.ApprovalTimeoutMinutes} min)
""");
        var components = new ComponentBuilder()
            .WithButton(vietnamese ? "Duyệt" : "Approve", $"agent-approve:{approval.Id}", ButtonStyle.Success)
            .WithButton(vietnamese ? "Từ chối" : "Deny", $"agent-deny:{approval.Id}", ButtonStyle.Danger)
            .Build();
        var message = await channel.SendMessageAsync(text, components: components);
        return new AgentMessageRef(channel.Id, message.Id);
    }

    public async Task PostApprovalResultAsync(AgentApprovalRecord approval, string text, CancellationToken cancellationToken)
    {
        if (ChannelForApproval(approval) is not { } channel)
        {
            return;
        }

        await channel.SendMessageAsync(Truncate(text));
    }

    private SocketTextChannel? Channel(string? instanceId)
    {
        var client = clients.Client;
        var discord = settings.Get().Discord;
        if (client is null)
        {
            return null;
        }

        var key = instanceId is { Length: > 0 } && discord.Channels.ContainsKey(instanceId) ? instanceId : "system";
        if (!discord.Channels.TryGetValue(key, out var raw) || !ulong.TryParse(raw, out var id))
        {
            return null;
        }

        return client.GetChannel(id) as SocketTextChannel;
    }

    private SocketTextChannel? ChannelFor(AgentIncidentRecord incident)
    {
        if (incident.ThreadId is { Length: > 0 } thread && ulong.TryParse(thread, out var threadId)
            && clients.Client?.GetChannel(threadId) is SocketThreadChannel socketThread)
        {
            return socketThread;
        }

        return Channel(incident.InstanceId);
    }

    private SocketTextChannel? ChannelForApproval(AgentApprovalRecord approval)
    {
        if (approval.Scope is { Length: > 0 } scope && scope.StartsWith("discord:", StringComparison.Ordinal)
            && ulong.TryParse(scope["discord:".Length..], out var channelId)
            && clients.Client?.GetChannel(channelId) is SocketTextChannel channel)
        {
            return channel;
        }

        return Channel(approval.InstanceId);
    }

    private static string Truncate(string text) => text.Length <= 1900 ? text : text[..1900] + "…";
}
