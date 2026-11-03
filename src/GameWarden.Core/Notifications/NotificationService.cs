using System.Text.Json;
using System.Threading.Channels;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using GameWarden.Core.Events;
using GameWarden.Core.Hosting;
using GameWarden.Core.Metrics;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Notifications;

/// <summary>
/// Turns activity/feature events into durable notification deliveries and sends them through the registered channels
/// (docs/18). Event handlers only enqueue; one worker owns SQLite and the channel calls.
/// </summary>
public sealed class NotificationService(
    IDbContextFactory<GameWardenDb> db,
    NotificationSettings settings,
    CoreEvents events,
    IEnumerable<INotificationChannel> channels,
    Supervisor supervisor,
    MetricsSampler metrics,
    TimeProvider time,
    ILogger<NotificationService> logger) : BackgroundService
{
    public const int MaxAttempts = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Channel<NotificationEventRecord> _events = Channel.CreateBounded<NotificationEventRecord>(
        new BoundedChannelOptions(2000) { SingleWriter = false, SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly IReadOnlyDictionary<string, INotificationChannel> _channels =
        channels.ToDictionary(channel => channel.Id, StringComparer.Ordinal);

    public void Publish(NotificationEventRecord notification) => _events.Writer.TryWrite(notification);

    public void Publish(
        string topic,
        string severity,
        string title,
        string? body = null,
        IReadOnlyList<NotificationField>? fields = null,
        string? instanceId = null,
        string? jobId = null,
        string? url = null,
        string? dedupeKey = null,
        string source = "core") =>
        Publish(new NotificationEventRecord
        {
            At = time.GetUtcNow().UtcDateTime,
            Topic = topic,
            Severity = severity,
            Title = title,
            Body = body,
            FieldsJson = fields is null ? null : JsonSerializer.Serialize(fields, Json),
            InstanceId = instanceId,
            JobId = jobId,
            Url = url,
            DedupeKey = dedupeKey,
            Source = source,
        });

    public async Task<NotificationSendResult> TestAsync(string destinationId, CancellationToken cancellationToken)
    {
        var destination = settings.Find(destinationId);
        if (destination is null || !_channels.TryGetValue(destination.Channel, out var channel))
        {
            return new NotificationSendResult(false, Error: "No such destination or channel.");
        }

        return await channel.SendAsync(destination, new NotificationEventRecord
        {
            At = time.GetUtcNow().UtcDateTime,
            Topic = "system.test",
            Severity = "info",
            Title = "GameWarden test",
            Body = "Discord notifications are configured.",
            Source = "system",
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDeliveryView>> DeliveriesAsync(int? limit, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var rows = await (from delivery in context.NotificationDeliveries.AsNoTracking()
                          join notification in context.NotificationEvents.AsNoTracking() on delivery.EventId equals notification.Id
                          orderby delivery.Id descending
                          select new { delivery, notification }).Take(Math.Clamp(limit ?? 100, 1, 200)).ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(row => new NotificationDeliveryView(
                row.delivery.Id, row.delivery.EventId, row.delivery.DestinationId, row.notification.Topic,
                row.notification.Severity, row.notification.Title, row.delivery.State, row.delivery.Attempts,
                row.delivery.LastError, row.delivery.MessageId, Utc(row.notification.At), Utc(row.delivery.NextAttemptAt), Utc(row.delivery.SentAt))),
        ];
    }

    public async Task<bool> RetryAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var delivery = await context.NotificationDeliveries.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (delivery is null)
        {
            return false;
        }

        delivery.State = "pending";
        delivery.Attempts = 0;
        delivery.NextAttemptAt = time.GetUtcNow().UtcDateTime;
        delivery.LastError = null;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        events.ActivityAdded += OnActivity;
        await RestoreSendingAsync(stoppingToken);
        await Task.WhenAll(
            ProcessEventsAsync(stoppingToken),
            DispatchAsync(stoppingToken),
            StatusLoopAsync(stoppingToken));
    }

    /// <summary>A crash can leave a row in <c>sending</c>; make it due again instead of stranding it (docs/18 §7).</summary>
    private async Task RestoreSendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            await context.NotificationDeliveries
                .Where(delivery => delivery.State == "sending")
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(delivery => delivery.State, "pending")
                        .SetProperty(delivery => delivery.NextAttemptAt, time.GetUtcNow().UtcDateTime),
                    cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not restore in-flight notification deliveries.");
        }
    }

    private void OnActivity(ActivityRecord activity)
    {
        if (activity.Kind is "command" or "settings" or "config")
        {
            return;
        }

        var topic = activity.Kind switch
        {
            "update" => activity.Level == "info" ? "update.installed" : "update.failed",
            "core.started" when activity.Level != "info" => "core.recovered",
            _ => activity.Kind,
        };
        var severity = activity.Level switch { "error" => "error", "warning" => "warning", _ => "info" };
        if (NotificationSettings.Catalog.FirstOrDefault(entry => entry.Key == topic) is { } catalog
            && NotificationSettings.Rank(catalog.DefaultSeverity) > NotificationSettings.Rank(severity))
        {
            severity = catalog.DefaultSeverity;
        }

        Publish(
            topic,
            severity,
            activity.Message,
            instanceId: activity.InstanceId,
            jobId: activity.JobId,
            dedupeKey: $"{topic}:{activity.InstanceId}:{activity.JobId}:{activity.At:yyyyMMddHHmm}");
    }

    private async Task ProcessEventsAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in _events.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var context = await db.CreateDbContextAsync(stoppingToken);
                if (notification.DedupeKey is { Length: > 0 } key
                    && await context.NotificationEvents.AnyAsync(
                        existing => existing.DedupeKey == key && existing.At > notification.At.AddMinutes(-10),
                        stoppingToken))
                {
                    continue;
                }

                context.NotificationEvents.Add(notification);
                await context.SaveChangesAsync(stoppingToken);
                var destinations = settings.Get().Destinations
                    .Where(destination => destination.Mode != "status"
                        && NotificationSettings.Matches(destination, notification.Topic, notification.Severity, notification.InstanceId))
                    .ToList();
                foreach (var destination in destinations)
                {
                    context.NotificationDeliveries.Add(new NotificationDeliveryRecord
                    {
                        EventId = notification.Id,
                        DestinationId = destination.Id,
                        Mode = destination.Mode,
                        State = "pending",
                        NextAttemptAt = time.GetUtcNow().UtcDateTime,
                    });
                }

                await context.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not queue notification {Topic}.", notification.Topic);
            }
        }
    }

    private async Task DispatchAsync(CancellationToken stoppingToken)
    {
        var nextPrune = time.GetUtcNow();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (time.GetUtcNow() >= nextPrune)
                {
                    nextPrune = time.GetUtcNow().AddHours(1);
                    await PruneAsync(stoppingToken);
                }

                await using var context = await db.CreateDbContextAsync(stoppingToken);
                var now = time.GetUtcNow().UtcDateTime;
                var due = await context.NotificationDeliveries
                    .Where(delivery => delivery.State == "pending" && delivery.NextAttemptAt <= now)
                    .OrderBy(delivery => delivery.Id)
                    .Take(50)
                    .ToListAsync(stoppingToken);
                foreach (var delivery in due.Where(delivery => delivery.Mode == "instant"))
                {
                    await SendDeliveryAsync(context, delivery, stoppingToken);
                }

                foreach (var destinationId in due
                             .Where(delivery => delivery.Mode == "digest")
                             .Select(delivery => delivery.DestinationId)
                             .Distinct(StringComparer.Ordinal))
                {
                    await SendDigestAsync(context, destinationId, now, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification dispatch failed.");
            }
        }
    }

    /// <summary>Groups pending digest rows for one destination into one embed (docs/18 §4.5).</summary>
    private async Task SendDigestAsync(
        GameWardenDb context,
        string destinationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var deliveries = await context.NotificationDeliveries
            .Where(delivery => delivery.State == "pending" && delivery.Mode == "digest"
                && delivery.DestinationId == destinationId && delivery.NextAttemptAt <= now)
            .OrderBy(delivery => delivery.Id)
            .ToListAsync(cancellationToken);
        if (deliveries.Count == 0)
        {
            return;
        }

        var eventIds = deliveries.Select(delivery => delivery.EventId).ToHashSet();
        var events = await context.NotificationEvents.AsNoTracking()
            .Where(notification => eventIds.Contains(notification.Id))
            .OrderBy(notification => notification.At)
            .ToListAsync(cancellationToken);
        var destination = settings.Find(destinationId);
        if (events.Count == 0 || destination is null || !destination.Enabled || !_channels.TryGetValue(destination.Channel, out var channel))
        {
            Drop(deliveries, destination is null ? "The destination no longer exists." : "The event or channel no longer exists.");
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (now - events[0].At < TimeSpan.FromSeconds(destination.DigestSec))
        {
            return;
        }

        var digest = Digest(events);
        foreach (var delivery in deliveries)
        {
            delivery.State = "sending";
        }

        await context.SaveChangesAsync(cancellationToken);
        NotificationSendResult result;
        try
        {
            result = await channel.SendAsync(destination, digest, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new NotificationSendResult(false, Error: ex.Message);
        }

        Apply(deliveries, result, destination.Id, now);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static NotificationEventRecord Digest(IReadOnlyList<NotificationEventRecord> events)
    {
        var ordered = events.OrderBy(notification => notification.At).ToList();
        var lines = ordered.Select(notification =>
        {
            var instance = notification.InstanceId is { Length: > 0 } id ? $" · {id}" : string.Empty;
            return $"[{notification.Severity}] {notification.Title}{instance}";
        });
        var body = string.Join('\n', lines);
        if (body.Length > 3900)
        {
            body = body[..3899] + "…";
        }

        var severity = ordered
            .OrderByDescending(notification => NotificationSettings.Rank(notification.Severity))
            .First()
            .Severity;
        return new NotificationEventRecord
        {
            At = ordered[0].At,
            Topic = "digest",
            Severity = severity,
            Title = $"{ordered.Count} GameWarden events",
            Body = body,
            Source = "digest",
        };
    }

    private void Apply(
        IReadOnlyList<NotificationDeliveryRecord> deliveries,
        NotificationSendResult result,
        string destinationId,
        DateTime now)
    {
        foreach (var delivery in deliveries)
        {
            if (result.Ok)
            {
                delivery.State = "sent";
                delivery.MessageId = result.MessageId;
                delivery.SentAt = now;
                delivery.LastError = null;
            }
            else if (result.Drop)
            {
                delivery.State = "dropped";
                delivery.LastError = result.Error;
            }
            else if (result.Disable)
            {
                delivery.State = "failed";
                delivery.LastError = result.Error;
            }
            else if (delivery.Attempts >= MaxAttempts)
            {
                delivery.State = "failed";
                delivery.LastError = result.Error;
            }
            else
            {
                delivery.Attempts++;
                delivery.LastError = result.Error;
                delivery.State = "pending";
                delivery.NextAttemptAt = now + (result.RetryAfter ?? Backoff(delivery.Attempts));
            }
        }

        if (result.Disable)
        {
            Disable(destinationId);
        }
    }

    private static void Drop(IReadOnlyList<NotificationDeliveryRecord> deliveries, string reason)
    {
        foreach (var delivery in deliveries)
        {
            delivery.State = "dropped";
            delivery.LastError = reason;
        }
    }

    /// <summary>One editable status message per status destination; unchanged content is not sent again (docs/18 §5).</summary>
    private async Task StatusLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await UpdateStatusesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification status update failed.");
            }
        }
    }

    private async Task UpdateStatusesAsync(CancellationToken cancellationToken)
    {
        var destinations = settings.Get().Destinations
            .Where(destination => destination.Enabled && destination.Mode == "status" && _channels.ContainsKey(destination.Channel))
            .ToList();
        if (destinations.Count == 0)
        {
            return;
        }

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var destination in destinations)
        {
            var row = await context.NotificationStatuses
                .FirstOrDefaultAsync(status => status.DestinationId == destination.Id, cancellationToken);
            if (row?.UpdatedAt is { } updated
                && now.UtcDateTime - updated < TimeSpan.FromSeconds(destination.IntervalSec))
            {
                continue;
            }

            var content = StatusText();
            if (row is { MessageId: not null } && row.Content == content)
            {
                row.UpdatedAt = now.UtcDateTime;
                await context.SaveChangesAsync(cancellationToken);
                continue;
            }

            var channel = _channels[destination.Channel];
            var notification = new NotificationEventRecord
            {
                At = now.UtcDateTime,
                Topic = "status",
                Severity = "info",
                Title = "GameWarden status",
                Body = content,
                Source = "status",
            };
            NotificationSendResult result;
            if (row?.MessageId is { } messageId)
            {
                result = await channel.UpdateAsync(destination, messageId, notification, cancellationToken);
                if (result.Missing)
                {
                    row.MessageId = null;
                    row.Content = null;
                    row.UpdatedAt = null;
                    await context.SaveChangesAsync(cancellationToken);
                    continue;
                }
            }
            else
            {
                result = await channel.SendAsync(destination, notification, cancellationToken);
                if (result.Ok)
                {
                    row ??= NewStatusRow(context, destination.Id);
                    row.MessageId = result.MessageId;
                }
            }

            row ??= NewStatusRow(context, destination.Id);
            row.UpdatedAt = now.UtcDateTime;
            if (result.Ok)
            {
                row.Content = content;
            }

            if (result.Disable)
            {
                Disable(destination.Id);
            }
            else if (!result.Ok)
            {
                logger.LogWarning("Status message for {Destination} was not updated: {Reason}", destination.Id, result.Error);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static NotificationStatusRecord NewStatusRow(GameWardenDb context, string destinationId)
    {
        var row = new NotificationStatusRecord { DestinationId = destinationId };
        context.NotificationStatuses.Add(row);
        return row;
    }

    private string StatusText()
    {
        var instances = supervisor.List();
        if (instances.Count == 0)
        {
            return "No servers configured.";
        }

        var samples = metrics.Report().Instances;
        return string.Join('\n', instances.Select(instance =>
        {
            var detail = samples.TryGetValue(instance.Id, out var series) && series.Latest is { } latest
                ? $" · {latest.CpuPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}% CPU · {latest.RamMb} MB"
                : string.Empty;
            return $"{instance.DisplayName} · {instance.Id}  {instance.State}{detail}";
        }));
    }

    private async Task SendDeliveryAsync(
        GameWardenDb context,
        NotificationDeliveryRecord delivery,
        CancellationToken cancellationToken)
    {
        var notification = await context.NotificationEvents.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == delivery.EventId, cancellationToken);
        var destination = settings.Find(delivery.DestinationId);
        if (notification is null || destination is null || !destination.Enabled || !_channels.TryGetValue(destination.Channel, out var channel))
        {
            delivery.State = "dropped";
            delivery.LastError = "The destination, event or channel no longer exists, or the destination is disabled.";
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        delivery.State = "sending";
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            var result = await channel.SendAsync(destination, notification, cancellationToken);
            if (result.Ok)
            {
                delivery.State = "sent";
                delivery.MessageId = result.MessageId;
                delivery.SentAt = time.GetUtcNow().UtcDateTime;
                delivery.LastError = null;
            }
            else if (result.Drop)
            {
                delivery.State = "dropped";
                delivery.LastError = result.Error;
            }
            else if (result.Disable)
            {
                delivery.State = "failed";
                delivery.LastError = result.Error;
                Disable(destination.Id);
            }
            else if (delivery.Attempts >= MaxAttempts)
            {
                delivery.State = "failed";
                delivery.LastError = result.Error;
            }
            else
            {
                delivery.Attempts++;
                delivery.LastError = result.Error;
                delivery.State = "pending";
                delivery.NextAttemptAt = time.GetUtcNow().UtcDateTime + (result.RetryAfter ?? Backoff(delivery.Attempts));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.Attempts++;
            delivery.LastError = ex.Message;
            delivery.State = delivery.Attempts >= MaxAttempts ? "failed" : "pending";
            delivery.NextAttemptAt = time.GetUtcNow().UtcDateTime + Backoff(delivery.Attempts);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Successes are kept for 7 days, failures and orphan events for 30 (docs/18 §4.4).</summary>
    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            var sentCutoff = now.AddDays(-7);
            var failureCutoff = now.AddDays(-30);
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            await context.NotificationDeliveries
                .Where(delivery => delivery.State == "sent" && delivery.SentAt < sentCutoff)
                .ExecuteDeleteAsync(cancellationToken);
            await context.NotificationEvents
                .Where(notification => notification.At < sentCutoff
                    && !context.NotificationDeliveries.Any(delivery => delivery.EventId == notification.Id))
                .ExecuteDeleteAsync(cancellationToken);
            await context.NotificationDeliveries
                .Where(delivery => context.NotificationEvents.Any(
                    notification => notification.Id == delivery.EventId && notification.At < failureCutoff))
                .ExecuteDeleteAsync(cancellationToken);
            await context.NotificationEvents
                .Where(notification => notification.At < failureCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var destinationIds = settings.Get().Destinations.Select(destination => destination.Id).ToHashSet(StringComparer.Ordinal);
            var staleStatuses = await context.NotificationStatuses
                .Where(status => !destinationIds.Contains(status.DestinationId))
                .ToListAsync(cancellationToken);
            if (staleStatuses.Count > 0)
            {
                context.NotificationStatuses.RemoveRange(staleStatuses);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not prune notification history.");
        }
    }

    private void Disable(string id)
    {
        try
        {
            var current = settings.Get();
            var destinations = current.Destinations
                .Select(destination => destination.Id == id ? destination with { Enabled = false } : destination)
                .ToList();
            settings.Update(destinations);
            logger.LogWarning("Notification destination {Destination} was disabled after a permanent Discord error.", id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotificationSettingsException)
        {
            logger.LogWarning("Could not disable notification destination {Destination}: {Reason}", id, ex.Message);
        }
    }

    private static DateTimeOffset Utc(DateTime value) => new(value.Ticks, TimeSpan.Zero);

    private static DateTimeOffset? Utc(DateTime? value) => value is { } time ? Utc(time) : null;

    private static TimeSpan Backoff(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromHours(1),
    };
}

public sealed record NotificationField(string Label, string Value, bool Inline = false);
