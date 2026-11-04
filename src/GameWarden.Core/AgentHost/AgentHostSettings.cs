using System.Text.Json;
using GameWarden.Core.Secrets;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// <c>agent-host.json</c>: the Owner's Operations Agent (docs/20, ADR-0030/0031). Secrets are never in this file;
/// they live in <see cref="SecretScopes.AgentHost"/>.
/// </summary>
public sealed class AgentHostSettingsDocument
{
    public int SchemaVersion { get; init; } = 1;

    public bool Enabled { get; set; }

    public AgentModelSettings Model { get; set; } = new();

    public AgentDiscordSettings Discord { get; set; } = new();

    public AgentAutonomySettings Autonomy { get; set; } = new();
}

public sealed class AgentModelSettings
{
    /// <summary>OpenAI-compatible base URL, for example <c>https://host/v1</c>; the client appends <c>/chat/completions</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Model { get; set; } = "deepseek-v4.1-flash";

    public int MaxTokens { get; set; } = 4096;

    public double Temperature { get; set; } = 0.2;

    public int MaxToolCalls { get; set; } = 12;

    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class AgentDiscordSettings
{
    public ulong GuildId { get; set; }

    /// <summary>Channel id per instance id, plus the special key <c>system</c>.</summary>
    public Dictionary<string, ulong> Channels { get; set; } = new(StringComparer.Ordinal);

    public List<ulong> AllowedUserIds { get; set; } = [];

    public List<ulong> AllowedRoleIds { get; set; } = [];

    public bool RequireMention { get; set; } = true;

    public string Language { get; set; } = "vi";
}

public sealed class AgentAutonomySettings
{
    /// <summary>Only <c>approve-writes</c> is accepted in this version; L3 actions come with the policy engine.</summary>
    public string Mode { get; set; } = "approve-writes";

    public int ApprovalTimeoutMinutes { get; set; } = 15;

    public int MaxWritesPerHour { get; set; } = 6;
}

/// <summary>The write-only body of <c>PUT /system/agent-host/secrets/{name}</c>.</summary>
public sealed record AgentHostSecret(string Value);

public sealed record AgentHostView(
    bool Enabled,
    bool ModelConfigured,
    bool ModelKeySet,
    bool BotTokenSet,
    bool BotConnected,
    string? LastError,
    AgentHostSettingsDocument Settings);

public sealed class AgentHostSettingsService(
    IOptions<GameWardenOptions> options,
    ISecretStore secrets,
    ILogger<AgentHostSettingsService> logger)
{
    public const string FileName = "agent-host.json";
    public const string ModelApiKeySecret = "model-api-key";
    public const string BotTokenSecret = "discord-bot-token";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string? LastError { get; private set; }

    public bool BotConnected { get; internal set; }

    public string PathOf() => Path.Combine(options.Value.DataRoot, FileName);

    public AgentHostSettingsDocument Get()
    {
        var path = PathOf();
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AgentHostSettingsDocument>(File.ReadAllText(path), Json) ?? new()
                : new AgentHostSettingsDocument();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not read {File}: {Reason}", FileName, ex.Message);
            return new AgentHostSettingsDocument();
        }
    }

    public AgentHostView View()
    {
        var settings = Get();
        return new AgentHostView(
            settings.Enabled,
            !string.IsNullOrWhiteSpace(settings.Model.BaseUrl) && !string.IsNullOrWhiteSpace(settings.Model.Model),
            !string.IsNullOrWhiteSpace(Secret(ModelApiKeySecret)),
            !string.IsNullOrWhiteSpace(Secret(BotTokenSecret)),
            BotConnected,
            LastError,
            settings);
    }

    public AgentHostSettingsDocument Update(AgentHostSettingsDocument next)
    {
        Validate(next);
        Directory.CreateDirectory(options.Value.DataRoot);
        var path = PathOf();
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(next, Json));
        File.Move(temporary, path, overwrite: true);
        return next;
    }

    public void SetSecret(string name, string value)
    {
        var clean = name.Trim().ToLowerInvariant();
        if (clean is not (ModelApiKeySecret or BotTokenSecret))
        {
            throw new AgentHostException($"secret must be {ModelApiKeySecret} or {BotTokenSecret}.");
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
        {
            throw new AgentHostException("secret value is empty or too long.");
        }

        secrets.Set(SecretScopes.AgentHost, clean, value.Trim());
    }

    public string? ModelApiKey() => Secret(ModelApiKeySecret);

    public string? BotToken() => Secret(BotTokenSecret);

    /// <summary>The instance id of a Discord channel, <c>system</c> for the general channel, or null when unmapped.</summary>
    public string? ChannelTarget(ulong channelId) =>
        Get().Discord.Channels.FirstOrDefault(pair => pair.Value == channelId).Key;

    public void Failed(Exception ex)
    {
        LastError = ex.Message;
        logger.LogWarning(ex, "Operations Agent: {Reason}", ex.Message);
    }

    public void ClearError() => LastError = null;

    private string? Secret(string name)
    {
        try
        {
            return secrets.Get(SecretScopes.AgentHost, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not read Agent secret {Name}: {Reason}", name, ex.Message);
            return null;
        }
    }

    private static void Validate(AgentHostSettingsDocument settings)
    {
        var model = settings.Model;
        if (model.MaxTokens is < 256 or > 32768)
        {
            throw new AgentHostException("model.maxTokens must be 256-32768.");
        }

        if (model.Temperature is < 0 or > 2)
        {
            throw new AgentHostException("model.temperature must be 0-2.");
        }

        if (model.MaxToolCalls is < 1 or > 32)
        {
            throw new AgentHostException("model.maxToolCalls must be 1-32.");
        }

        if (model.TimeoutSeconds is < 10 or > 600)
        {
            throw new AgentHostException("model.timeoutSeconds must be 10-600.");
        }

        if (!string.IsNullOrWhiteSpace(model.BaseUrl)
            && (!Uri.TryCreate(model.BaseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")))
        {
            throw new AgentHostException("model.baseUrl must be an absolute http(s) URL.");
        }

        if (settings.Discord.Language is not ("vi" or "en"))
        {
            throw new AgentHostException("discord.language must be vi or en.");
        }

        if (settings.Discord.Channels.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value == 0))
        {
            throw new AgentHostException("every Discord channel needs a non-empty instance key and a channel id.");
        }

        if (settings.Discord.Channels.Values.Distinct().Count() != settings.Discord.Channels.Count)
        {
            throw new AgentHostException("a Discord channel is mapped twice.");
        }

        if (settings.Autonomy.Mode != "approve-writes")
        {
            throw new AgentHostException("autonomy.mode must be approve-writes in this version.");
        }

        if (settings.Autonomy.ApprovalTimeoutMinutes is < 1 or > 1440)
        {
            throw new AgentHostException("autonomy.approvalTimeoutMinutes must be 1-1440.");
        }
    }
}

public sealed class AgentHostException(string message) : Exception(message);
