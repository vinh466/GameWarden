using GameWarden.Core.Notifications;

namespace GameWarden.Core.Api;

public sealed record NotificationDestinationView(
    string Id,
    string Name,
    string Channel,
    bool Enabled,
    string Mode,
    string Language,
    string MinimumSeverity,
    IReadOnlyList<string> Topics,
    IReadOnlyList<string> Instances,
    string? MentionRoleId,
    string? PanelUrl,
    int IntervalSec,
    int DigestSec,
    bool WebhookSet);

public sealed record NotificationSettingsView(
    IReadOnlyList<NotificationDestinationView> Destinations,
    IReadOnlyList<NotificationTopic> Catalog,
    IReadOnlyList<string> SupportedModes);

public sealed record NotificationDestinationUpdate(
    string Id,
    string Name,
    string Channel,
    bool Enabled,
    string Mode,
    string Language,
    string MinimumSeverity,
    IReadOnlyList<string>? Topics,
    IReadOnlyList<string>? Instances,
    string? MentionRoleId,
    string? PanelUrl,
    int IntervalSec,
    int DigestSec,
    string? WebhookUrl);

public sealed record NotificationSettingsUpdate(IReadOnlyList<NotificationDestinationUpdate>? Destinations);

/// <summary>The status message the <c>status</c> mode would edit in place (docs/18 §6).</summary>
public sealed record NotificationStatusPreview(string Text);

public sealed record NotificationDeliveryView(
    long Id,
    long EventId,
    string DestinationId,
    string Topic,
    string Severity,
    string Title,
    string State,
    int Attempts,
    string? LastError,
    string? MessageId,
    DateTimeOffset At,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? SentAt);
