using System.Text.Json;
using GameWarden.Core.Hosting;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Notifications;

public sealed record NotificationDestination(
    string Id,
    string Name,
    string Channel,
    string Secret,
    bool Enabled = true,
    string Mode = "instant",
    string Language = "vi",
    string MinimumSeverity = "warning",
    IReadOnlyList<string>? Topics = null,
    IReadOnlyList<string>? Instances = null,
    string? MentionRoleId = null,
    string? PanelUrl = null,
    int IntervalSec = 60,
    int DigestSec = 300);

public sealed record NotificationSettingsDocument(
    int SchemaVersion,
    IReadOnlyList<NotificationDestination> Destinations);

public sealed record NotificationTopic(string Key, string Category, string DefaultSeverity, bool Digest = false);

/// <summary>
/// The notification destinations and topic catalog (docs/18). The JSON file holds no webhook URL: each destination
/// names a secret in <see cref="Secrets.SecretScopes.Notifications"/>. A missing file means notifications are off.
/// </summary>
public sealed class NotificationSettings(IOptions<GameWardenOptions> options, ILogger<NotificationSettings> logger)
{
    public const string FileName = "notifications.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Modes = ["instant", "digest", "status"];
    public static readonly IReadOnlyList<string> SupportedModes = ["instant", "digest", "status"];
    private static readonly string[] Languages = ["en", "vi"];
    private static readonly string[] Severities = ["info", "success", "warning", "error", "critical"];

    public static readonly IReadOnlyList<NotificationTopic> Catalog =
    [
        new("instance.created", "instance", "info"),
        new("instance.installed", "instance", "info"),
        new("instance.running", "instance", "info"),
        new("instance.stopped", "instance", "info"),
        new("instance.crashed", "instance", "error"),
        new("instance.faulted", "instance", "critical"),
        new("instance.deleted", "instance", "info"),
        new("job.succeeded", "job", "info"),
        new("job.failed", "job", "error"),
        new("mods.sync.failed", "mods", "error"),
        new("mods.update.available", "mods", "info"),
        new("update.available", "update", "warning"),
        new("update.installed", "update", "info"),
        new("update.failed", "update", "error"),
        new("core.started", "system", "info"),
        new("core.recovered", "system", "warning"),
        new("disk.low", "system", "warning"),
        new("player.joined", "player", "info", Digest: true),
        new("player.left", "player", "info", Digest: true),
        new("admin.granted", "admin", "info"),
        new("admin.revoked", "admin", "info"),
    ];

    public NotificationSettingsDocument Get()
    {
        var path = PathOf();
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<NotificationSettingsDocument>(File.ReadAllText(path), Json)
                    ?? new NotificationSettingsDocument(1, [])
                : new NotificationSettingsDocument(1, []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not read notifications.json: {Reason}", ex.Message);
            return new NotificationSettingsDocument(1, []);
        }
    }

    public NotificationSettingsDocument Update(IReadOnlyList<NotificationDestination> destinations)
    {
        var clean = new List<NotificationDestination>();
        foreach (var destination in destinations)
        {
            var id = destination.Id.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9-]{2,64}$"))
            {
                throw new NotificationSettingsException("destination id must be 2-64 lowercase letters, digits or '-'.");
            }

            if (clean.Any(existing => existing.Id == id))
            {
                throw new NotificationSettingsException($"destination '{id}' is listed twice.");
            }

            if (string.IsNullOrWhiteSpace(destination.Name) || destination.Name.Length > 80)
            {
                throw new NotificationSettingsException($"destination '{id}' needs a name of at most 80 characters.");
            }

            if (destination.Channel != "discord-webhook")
            {
                throw new NotificationSettingsException("channel must be discord-webhook in this version.");
            }

            if (string.IsNullOrWhiteSpace(destination.Secret) || !Secrets.SecretScopes.IsValidName(destination.Secret))
            {
                throw new NotificationSettingsException($"destination '{id}' needs a valid secret name.");
            }

            if (!Modes.Contains(destination.Mode, StringComparer.Ordinal))
            {
                throw new NotificationSettingsException($"destination '{id}' mode must be {string.Join(", ", Modes)}.");
            }

            if (!Languages.Contains(destination.Language, StringComparer.Ordinal))
            {
                throw new NotificationSettingsException($"destination '{id}' language must be en or vi.");
            }

            if (!Severities.Contains(destination.MinimumSeverity, StringComparer.Ordinal))
            {
                throw new NotificationSettingsException($"destination '{id}' minimumSeverity must be {string.Join(", ", Severities)}.");
            }

            if (destination.IntervalSec is < 10 or > 3600 || destination.DigestSec is < 30 or > 3600)
            {
                throw new NotificationSettingsException($"destination '{id}' intervals must be 10-3600 seconds.");
            }

            if (!string.IsNullOrWhiteSpace(destination.MentionRoleId)
                && (destination.MentionRoleId.Length is < 17 or > 20 || !destination.MentionRoleId.All(char.IsAsciiDigit)))
            {
                throw new NotificationSettingsException($"destination '{id}' mentionRoleId must be a Discord role id.");
            }

            if (!string.IsNullOrWhiteSpace(destination.PanelUrl)
                && (!Uri.TryCreate(destination.PanelUrl, UriKind.Absolute, out var panel)
                    || panel.Scheme is not ("http" or "https")))
            {
                throw new NotificationSettingsException($"destination '{id}' panelUrl must be an http(s) URL.");
            }

            clean.Add(destination with { Id = id, Topics = destination.Topics ?? [], Instances = destination.Instances ?? ["*"] });
        }

        var document = new NotificationSettingsDocument(1, clean);
        var path = PathOf();
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, Json));
        File.Move(temporary, path, overwrite: true);
        return document;
    }

    public NotificationDestination? Find(string id) => Get().Destinations.FirstOrDefault(destination => destination.Id == id);

    public string PathOf() => Path.Combine(options.Value.DataRoot, FileName);

    public static bool IsWebhookUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host is "discord.com" or "discordapp.com"
        && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);

    public static bool Matches(NotificationDestination destination, string topic, string severity, string? instanceId)
    {
        if (!destination.Enabled)
        {
            return false;
        }

        if (instanceId is not null
            && destination.Instances is { Count: > 0 } instances
            && !instances.Contains("*", StringComparer.Ordinal)
            && !instances.Contains(instanceId, StringComparer.Ordinal))
        {
            return false;
        }

        if (Rank(severity) < Rank(destination.MinimumSeverity))
        {
            return false;
        }

        return destination.Topics is not { Count: > 0 } topics
            || topics.Any(filter => filter == "*"
                || filter == topic
                || (filter.EndsWith(".*", StringComparison.Ordinal) && topic.StartsWith(filter[..^1], StringComparison.Ordinal)));
    }

    public static int Rank(string severity) => severity switch
    {
        "success" => 1,
        "warning" => 2,
        "error" => 3,
        "critical" => 4,
        _ => 0,
    };
}

public sealed class NotificationSettingsException(string message) : Exception(message);
