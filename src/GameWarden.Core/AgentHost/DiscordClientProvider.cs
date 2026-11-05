using Discord.WebSocket;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// The live Discord client shared by the gateway bot and the notification/incident messenger (docs/21 §2.1). Keeping
/// the client here avoids a service cycle: the bot starts the client, the messenger only sends.
/// </summary>
public sealed class DiscordClientProvider
{
    private DiscordSocketClient? _client;

    public DiscordSocketClient? Client => Volatile.Read(ref _client);

    public bool Connected => Client?.ConnectionState == Discord.ConnectionState.Connected;

    public void Set(DiscordSocketClient? client) => Volatile.Write(ref _client, client);
}
