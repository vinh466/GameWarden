using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// The Owner's Discord bot (docs/20, ADR-0031): Gateway connection, one channel per server, mention chat, slash
/// commands and approval buttons. It never exposes a public endpoint; every write waits for a button press.
/// </summary>
public sealed partial class DiscordBotService(
    AgentHostSettingsService settings,
    AgentRuntime runtime,
    AgentApprovalService approvals,
    ILogger<DiscordBotService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _channelLocks = new();
    private DiscordSocketClient? _client;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Get().Enabled)
        {
            logger.LogInformation("Operations Agent is off.");
            return;
        }

        var token = settings.BotToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Operations Agent: Discord bot token is not set.");
            return;
        }

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Warning,
            MessageCacheSize = 50,
        };
        _client = new DiscordSocketClient(config);
        _client.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.MessageReceived += OnMessageAsync;
        _client.InteractionCreated += OnInteractionAsync;

        try
        {
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            settings.ClearError();
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Core is stopping.
        }
        catch (Exception ex)
        {
            settings.Failed(ex);
            logger.LogError(ex, "Operations Agent could not start Discord.");
        }
        finally
        {
            settings.BotConnected = false;
            if (_client is not null)
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
                _client.Dispose();
                _client = null;
            }
        }
    }

    /// <summary>
    /// Send one real message through the configured token and channel, without opening the Gateway. Used by the
    /// panel's <em>Test Discord</em> button before the bot is enabled.
    /// </summary>
    public async Task<AgentDiscordTestResult> TestAsync(ulong channelId, CancellationToken cancellationToken)
    {
        var host = settings.Get();
        var token = settings.BotToken();
        var checks = new List<AgentTestCheck>
        {
            new("Bot token", !string.IsNullOrWhiteSpace(token),
                string.IsNullOrWhiteSpace(token) ? "not set" : "set"),
            new("Guild", !string.IsNullOrWhiteSpace(host.Discord.GuildId),
                host.Discord.GuildId),
            new("Channel id", channelId > 0, channelId.ToString(CultureInfo.InvariantCulture)),
        };
        var started = Environment.TickCount64;
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Discord test for channel {ChannelId}: bot token is not set.", channelId);
            return new AgentDiscordTestResult(false, checks, Environment.TickCount64 - started);
        }

        Discord.Rest.DiscordRestClient? rest = null;
        try
        {
            if (_client?.CurrentUser is not null && _client.GetChannel(channelId) is IMessageChannel live)
            {
                var liveMessage = await live.SendMessageAsync("GameWarden test — the bot is connected. ✅");
                checks.Add(new("Gateway", true, $"connected as {_client.CurrentUser.Username}"));
                checks.Add(new("Message", true, $"#{live.Name} (id {liveMessage.Id})"));
                logger.LogInformation("Discord test for channel {ChannelId}: ok via Gateway ({Channel}).", channelId, live.Name);
                return new AgentDiscordTestResult(true, checks, Environment.TickCount64 - started);
            }

            rest = new Discord.Rest.DiscordRestClient(new Discord.Rest.DiscordRestConfig
            {
                LogLevel = LogSeverity.Warning,
            });
            await rest.LoginAsync(TokenType.Bot, token);
            checks.Add(new("Discord login", true, rest.CurrentUser?.Username));
            var channel = await ((IDiscordClient)rest).GetChannelAsync(channelId);
            if (channel is null)
            {
                checks.Add(new(
                    "Channel",
                    false,
                    $"bot cannot see {channelId}; check the id is a text channel in guild {host.Discord.GuildId} and the bot can view it"));
                return new AgentDiscordTestResult(false, checks, Environment.TickCount64 - started);
            }

            if (channel is not Discord.Rest.IRestMessageChannel target)
            {
                var kind = channel is IChannel generic ? generic.ChannelType.ToString() : "unknown";
                var name = channel is IChannel named ? named.Name : channelId.ToString(CultureInfo.InvariantCulture);
                checks.Add(new(
                    "Channel",
                    false,
                    $"{name} is a {kind} channel, not a text channel; copy the id of a text channel"));
                return new AgentDiscordTestResult(false, checks, Environment.TickCount64 - started);
            }

            checks.Add(new("Channel", true, $"#{target.Name}"));
            var sent = await target.SendMessageAsync("GameWarden test — the bot can write here. ✅");
            checks.Add(new("Message", true, $"#{target.Name} (id {sent.Id})"));
            logger.LogInformation("Discord test for channel {ChannelId}: ok via REST ({Channel}).", channelId, target.Name);
            return new AgentDiscordTestResult(true, checks, Environment.TickCount64 - started);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            checks.Add(new("Discord", false, ex.Message));
            logger.LogWarning(ex, "Discord test for channel {ChannelId} failed.", channelId);
            return new AgentDiscordTestResult(false, checks, Environment.TickCount64 - started);
        }
        finally
        {
            if (rest is not null)
            {
                await rest.LogoutAsync();
                rest.Dispose();
            }
        }
    }

    private Task OnLogAsync(LogMessage message)
    {
        if (message.Exception is not null)
        {
            logger.LogWarning(message.Exception, "Discord: {Message}", message.Message);
        }
        else
        {
            logger.LogDebug("Discord: {Message}", message.Message);
        }

        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        settings.BotConnected = true;
        settings.ClearError();
        logger.LogInformation("Operations Agent connected to Discord as {User}.", _client?.CurrentUser?.Username);
        await RegisterCommandsAsync();
    }

    private async Task RegisterCommandsAsync()
    {
        if (_client is null)
        {
            return;
        }

        if (!ulong.TryParse(settings.Get().Discord.GuildId, out var guildId)
            || _client.GetGuild(guildId) is not { } guild)
        {
            logger.LogWarning("Operations Agent: Discord guild id is not configured.");
            return;
        }

        SlashCommandProperties[] commands =
        [
            new SlashCommandBuilder().WithName("ask").WithDescription("Hỏi Operations Agent")
                .AddOption("message", ApplicationCommandOptionType.String, "Câu hỏi hoặc yêu cầu", isRequired: true).Build(),
            new SlashCommandBuilder().WithName("status").WithDescription("Tóm tắt trạng thái GameWarden").Build(),
            new SlashCommandBuilder().WithName("server").WithDescription("Kiểm tra một game server")
                .AddOption("id", ApplicationCommandOptionType.String, "Instance id; bỏ trống để dùng kênh hiện tại", isRequired: false).Build(),
            new SlashCommandBuilder().WithName("logs").WithDescription("Đọc log của server")
                .AddOption("instance", ApplicationCommandOptionType.String, "Instance id", isRequired: false)
                .AddOption("tail", ApplicationCommandOptionType.Integer, "Số dòng cuối (1-1000)", isRequired: false)
                .AddOption("level", ApplicationCommandOptionType.String, "trace/debug/info/warn/error/critical", isRequired: false)
                .AddOption("contains", ApplicationCommandOptionType.String, "Text cần tìm", isRequired: false).Build(),
            new SlashCommandBuilder().WithName("diagnose").WithDescription("Chẩn đoán sâu một server")
                .AddOption("instance", ApplicationCommandOptionType.String, "Instance id", isRequired: false).Build(),
            new SlashCommandBuilder().WithName("approve").WithDescription("Duyệt một thao tác đang chờ")
                .AddOption("id", ApplicationCommandOptionType.String, "Approval id", isRequired: true).Build(),
            new SlashCommandBuilder().WithName("deny").WithDescription("Từ chối một thao tác đang chờ")
                .AddOption("id", ApplicationCommandOptionType.String, "Approval id", isRequired: true).Build(),
            new SlashCommandBuilder().WithName("stop").WithDescription("Tắt Operations Agent (kill switch)").Build(),
            new SlashCommandBuilder().WithName("start").WithDescription("Bật lại Operations Agent").Build(),
        ];

        foreach (var command in commands)
        {
            try
            {
                await guild.CreateApplicationCommandAsync(command);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not register Discord command.");
            }
        }
    }

    private async Task OnMessageAsync(SocketMessage message)
    {
        if (message.Author.IsBot || message is not SocketUserMessage userMessage || _client is null)
        {
            return;
        }

        var channel = message.Channel;
        var rawTarget = Target(channel.Id);
        if (rawTarget is null || !settings.Get().Enabled)
        {
            logger.LogDebug("Discord message in {ChannelId} ignored: channel is not mapped.", channel.Id);
            return;
        }

        var target = rawTarget == "system" ? null : rawTarget;

        var mentioned = message.MentionedUsers.Any(user => user.Id == _client.CurrentUser.Id);
        if (settings.Get().Discord.RequireMention && !mentioned)
        {
            logger.LogDebug("Discord message in {ChannelId} ignored: the bot was not mentioned.", channel.Id);
            return;
        }

        if (message.Author is not SocketGuildUser user || !Allowed(user))
        {
            logger.LogInformation("Discord message from {User} ignored: user is not in the allowlist.", message.Author.Id);
            if (mentioned)
            {
                await channel.SendMessageAsync("Bạn không có quyền dùng Operations Agent.");
            }

            return;
        }

        var text = Mention().Replace(message.Content, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var gate = _channelLocks.GetOrAdd(channel.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await channel.TriggerTypingAsync();
            var reply = await runtime.HandleAsync(channel.Id, target, user.Username, text, CancellationToken.None);
            await SendLongAsync(channel, reply.Text);
            if (reply.Approval is { } approval)
            {
                await SendApprovalAsync(channel, approval, reply.Text);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Operations Agent failed to answer a Discord message.");
            await channel.SendMessageAsync($"Lỗi: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task OnInteractionAsync(SocketInteraction interaction)
    {
        if (interaction is SocketMessageComponent component)
        {
            await OnComponentAsync(component);
            return;
        }

        if (interaction is not SocketSlashCommand command || command.User is not SocketGuildUser user)
        {
            return;
        }

        if (!settings.Get().Enabled && command.Data.Name is not ("start" or "stop"))
        {
            await command.RespondAsync("Operations Agent đang tắt. Dùng `/start` để bật lại.", ephemeral: true);
            return;
        }

        if (!Allowed(user))
        {
            await command.RespondAsync("Bạn không có quyền dùng Operations Agent.", ephemeral: true);
            return;
        }

        logger.LogInformation("Discord slash {Command} from {User} in {ChannelId}.", command.Data.Name, user.Username, command.ChannelId);
        try
        {
            await command.DeferAsync(ephemeral: false);
            var id = command.Data.Name switch
            {
                "stop" => await StopAgentAsync(),
                "start" => await StartAgentAsync(),
                "approve" => await DecideAsync(Option(command, "id"), user, approve: true),
                "deny" => await DecideAsync(Option(command, "id"), user, approve: false),
                _ => await AskAsync(command, user),
            };
            await command.FollowupAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Operations Agent slash command failed.");
            await command.FollowupAsync($"Lỗi: {ex.Message}");
        }
    }

    private async Task<string> AskAsync(SocketSlashCommand command, SocketGuildUser user)
    {
        var rawTarget = Target(command.ChannelId ?? 0);
        var target = rawTarget == "system" ? null : rawTarget;
        var text = command.Data.Name switch
        {
            "ask" => Option(command, "message") ?? string.Empty,
            "status" => "Cho tôi tình trạng hiện tại của GameWarden.",
            "server" => $"Kiểm tra server {Option(command, "id") ?? target ?? "đang có"}.",
            "logs" => $"Đọc log của {Option(command, "instance") ?? target ?? "server đang chọn"}"
                + (Option(command, "tail") is { } tail ? $", {tail} dòng cuối" : string.Empty)
                + (Option(command, "level") is { } level ? $", level {level}" : string.Empty)
                + (Option(command, "contains") is { } contains ? $", chứa \"{contains}\"" : string.Empty) + ".",
            "diagnose" => $"Chẩn đoán sâu server {Option(command, "instance") ?? target ?? "đang chọn"}: kiểm tra state, job, log, metric và diagnostics.",
            _ => string.Empty,
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Mình chưa hiểu lệnh này.";
        }

        var reply = await runtime.HandleAsync(command.ChannelId ?? 0, Option(command, "instance") ?? target, user.Username, text, CancellationToken.None);
        if (reply.Approval is { } approval)
        {
            var channel = command.Channel ?? _client?.GetChannel(command.ChannelId ?? 0) as IMessageChannel;
            if (channel is not null)
            {
                await SendApprovalAsync(channel, approval, reply.Text);
            }
        }

        return reply.Text;
    }

    private async Task OnComponentAsync(SocketMessageComponent component)
    {
        if (component.User is not SocketGuildUser user || !Allowed(user))
        {
            await component.RespondAsync("Bạn không có quyền duyệt thao tác này.", ephemeral: true);
            return;
        }

        var parts = component.Data.CustomId.Split(':', 2);
        if (parts.Length != 2 || parts[0] is not ("agent-approve" or "agent-deny"))
        {
            return;
        }

        var approve = parts[0] == "agent-approve";
        var result = approve
            ? await approvals.ApproveAsync(parts[1], user.Username, CancellationToken.None)
            : await approvals.DenyAsync(parts[1], user.Username, CancellationToken.None);
        var text = approve
            ? result.Error is null
                ? $"Đã duyệt `{result.Approval.Tool}`.\n```\n{Truncate(result.Result ?? "Xong.")}\n```"
                : $"Không chạy được `{result.Approval.Tool}`: {result.Error}"
            : "Đã từ chối thao tác.";
        await component.UpdateAsync(message =>
        {
            message.Content = Truncate(text);
            message.Components = new ComponentBuilder().Build();
        });
    }

    private async Task<string> DecideAsync(string? id, SocketGuildUser user, bool approve)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Thiếu approval id.";
        }

        var result = approve
            ? await approvals.ApproveAsync(id, user.Username, CancellationToken.None)
            : await approvals.DenyAsync(id, user.Username, CancellationToken.None);
        return approve
            ? result.Error ?? $"Đã duyệt `{result.Approval.Tool}`.\n```\n{Truncate(result.Result ?? "Xong.")}\n```"
            : "Đã từ chối thao tác.";
    }

    private Task<string> StopAgentAsync()
    {
        var next = settings.Get();
        next.Enabled = false;
        settings.Update(next);
        return Task.FromResult("Operations Agent đã tắt. Dùng `/start` để bật lại.");
    }

    private Task<string> StartAgentAsync()
    {
        var next = settings.Get();
        next.Enabled = true;
        settings.Update(next);
        return Task.FromResult("Operations Agent đã bật. Nếu bot chưa kết nối, khởi động lại Core.");
    }

    private Task SendApprovalAsync(IMessageChannel channel, Data.AgentApprovalRecord approval, string reason)
    {
        var components = new ComponentBuilder()
            .WithButton("Approve", $"agent-approve:{approval.Id}", ButtonStyle.Success)
            .WithButton("Deny", $"agent-deny:{approval.Id}", ButtonStyle.Danger)
            .Build();
        var text = Truncate($"**Cần duyệt:** `{approval.Tool}`\n{reason}\nHết hạn lúc {approval.ExpiresAt:HH:mm} UTC.");
        _ = Task.Run(async () =>
        {
            var message = await channel.SendMessageAsync(text, components: components);
            await approvals.SetMessageAsync(approval.Id, message.Id.ToString(), CancellationToken.None);
        });
        return Task.CompletedTask;
    }

    private async Task SendLongAsync(IMessageChannel channel, string text)
    {
        var remaining = text;
        while (remaining.Length > 0)
        {
            var take = Math.Min(1900, remaining.Length);
            var cut = remaining.LastIndexOf('\n', Math.Max(0, take - 1));
            if (cut < 1000)
            {
                cut = take;
            }

            await channel.SendMessageAsync(remaining[..cut]);
            remaining = remaining[cut..].TrimStart();
        }
    }

    /// <summary>The mapped key (<c>system</c> or an instance id), or null when the channel is not mapped.</summary>
    private string? Target(ulong channelId)
    {
        var target = settings.ChannelTarget(channelId);
        if (target is not null)
        {
            return target;
        }

        return _client?.GetChannel(channelId) is SocketThreadChannel thread
            ? settings.ChannelTarget(thread.ParentChannel.Id)
            : null;
    }

    private bool Allowed(SocketGuildUser user)
    {
        var discord = settings.Get().Discord;
        if (discord.AllowedUserIds.Contains(user.Id.ToString(CultureInfo.InvariantCulture)))
        {
            return true;
        }

        if (discord.AllowedRoleIds.Any(id =>
                ulong.TryParse(id, out var roleId) && user.Roles.Any(role => role.Id == roleId)))
        {
            return true;
        }

        return discord.AllowedUserIds.Count == 0
            && discord.AllowedRoleIds.Count == 0
            && user.Guild.OwnerId == user.Id;
    }

    private static string? Option(SocketSlashCommand command, string name) =>
        command.Data.Options.FirstOrDefault(option => option.Name == name)?.Value?.ToString();

    private static string Truncate(string text) => text.Length <= 1900 ? text : text[..1900] + "…";

    [GeneratedRegex(@"<@!?\d+>")]
    private static partial Regex Mention();
}
