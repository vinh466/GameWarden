# 18 · Notifications and Discord: one notification platform, Discord as the first adapter

**Status: Accepted (2026-11-04); implementation pending.** The backlog entry is [ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md); the current event model is [03](03-domain-model.md#6-events); the existing webhook research is S6 in [09](09-open-questions.md#spikes-needed-before-m1-answers-feed-back-into-the-spec).

## 1. Goal

Make GameWarden's events reachable outside the panel, starting with Discord, without letting Discord leak into every game or feature:

- a game adapter reports **what happened** (`player.joined`, `instance.crashed`, `job.failed`), not how to render a Discord message;
- a feature reports a new kind of event once and it becomes available to every notification channel;
- Discord has one adapter that owns embeds, mentions, rate limits, retries, digests and status messages;
- a future Slack, Telegram or desktop notifier is another channel adapter, not another event pipeline;
- every notification is observable and safe: a bad webhook, a deleted channel or a Discord outage never affects the game server or Core's state.

Non-goals for the first version: a Discord bot, slash commands, interactive buttons that start/stop servers, Discord login, and chat relay. Those need a different security surface and are a later decision.

## 2. What Discord actually offers (checked 2026-11-04)

From Discord's official docs:

| Capability | Fact | Consequence |
|---|---|---|
| Webhook execute | `POST /webhooks/{id}/{token}?wait=true` returns the created message, including its `id` | One-way notifications need no bot. `wait=true` is required for status editing and delivery confirmation. |
| Edit | `PATCH /webhooks/{id}/{token}/messages/{id}` edits a message the same webhook sent | A live status message is possible without a bot. |
| Pin | No webhook endpoint pins a message | The status message is pinned once by hand; the panel/setup says so. |
| Message limits | `content` 2000 characters; up to 10 embeds; embeds total 6000 characters; title 256, description 4096, 25 fields, field name/value 256/1024, footer 2048 | The adapter must truncate and split; never let a long game error break delivery. |
| Mentions | Webhooks parse only user mentions by default; `allowed_mentions` controls roles/everyone. `@everyone` needs permission | Default is `parse: []`; a role mention is an explicit per-destination choice. |
| Rate limits | Per-route buckets; `X-RateLimit-*`; 429 has `retry_after`; 404 webhook should not be retried; global limit 50 req/s for a bot, invalid requests can be temporarily restricted | Parse headers, one in-flight request per destination, exponential backoff, disable on 404/401/403. Do not hardcode a rate. |
| Threads/forum | `thread_id` targets a thread; `thread_name` creates a forum/media thread | Optional `thread_id` per destination later; not needed for v1. |
| Components | Non-application webhooks can use non-interactive components only with `with_components=true`; interactive components need an application-owned webhook/bot | v1 can use link buttons; buttons that call GameWarden need a bot, not a webhook. |
| Message size | 2000 content, 6000 embeds total | Digest and truncation are part of the adapter, not optional. |

Sources: [Execute Webhook](https://discord.com/developers/docs/resources/webhook#execute-webhook), [Embed limits](https://discord.com/developers/docs/resources/message#embed-object-embed-limits), [Allowed mentions](https://discord.com/developers/docs/resources/message#allowed-mentions-object), [Rate limits](https://discord.com/developers/docs/topics/rate-limits).

## 3. Current GameWarden events

Core already has the raw material:

- `CoreEvents`: `InstanceChanged`, `InstanceRemoved`, `ConsoleLine`, `JobChanged`, `ActivityAdded`, `MetricsSampled`.
- `ActivityLog` turns instance/job transitions into a persistent feed (`events` table) with kind, level, message, instance, job and user.
- The instance state machine knows `Running`, `Stopped`, `Crashed`, `Faulted`;
- `JobRecord` knows install/update/mods jobs, stage, progress and failure message;
- `UpdateService` knows a newer release; the Supervisor knows a crash;
- the game integrations (docs/16, docs/17) know player join/leave and admin changes.

The notification platform listens to these; it does not make services call Discord. Existing features that already emit an activity line are picked up by a bridge, so Discord can ship without editing every feature. High-value events (player events, later backups) call the publisher directly.

## 4. Architecture

```
 game adapter / feature          CoreEvents / ActivityRecord
        │                                  │
        └────────── NotificationEvent ◄─────┘ (bridge)
                              │
                    NotificationRules (topics, severity, instance, mode)
                              │
                    notification_events + notification_deliveries (SQLite)
                              │
                 NotificationDispatcher (queue, retry, digest, status)
                              │
                    INotificationChannel
                    ├── DiscordWebhookChannel (v1)
                    └── future Slack/Telegram/desktop
```

Key boundaries:

1. **The event is provider-neutral.** No `DiscordEmbed`, `webhook`, `role` or `channel` in the event.
2. **The channel adapter owns the wire format.** It decides how a title, body, fields and severity become a Discord embed, a Slack block, or a desktop toast.
3. **Routing is data.** Destinations and topic filters live in `notifications.json`; webhook URLs live in `ISecretStore`.
4. **Delivery is durable.** An outbox and delivery rows survive a Core restart; no in-memory queue as the source of truth.
5. **The dispatcher is the only place that talks to a channel.** Event handlers never await Discord.

### 4.1 Event contract

```csharp
public sealed record NotificationEvent(
    string Topic,                         // instance.crashed, player.joined, ...
    NotificationSeverity Severity,        // info | success | warning | error | critical
    string Title,                         // already human-readable, short
    string? Body = null,                  // markdown-ish plain text, adapter sanitizes
    IReadOnlyList<NotificationField>? Fields = null,
    string? InstanceId = null,
    string? JobId = null,
    string? Url = null,                   // panel link, optional
    string? DedupeKey = null,             // same key in a window is one event
    IReadOnlyDictionary<string, string>? Data = null);

public sealed record NotificationField(string Label, string Value, bool Inline = false);
```

A feature publishes through:

```csharp
notifications.Publish(new NotificationEvent(
    "player.joined",
    NotificationSeverity.Info,
    $"{player.Name} joined",
    Fields: [new("Server", server.DisplayName), new("Platform ID", player.Id)],
    InstanceId: server.Id,
    DedupeKey: $"player.joined:{server.Id}:{player.Id}"));
```

`Publish` is non-blocking: it validates, then writes the event and fan-out rows. It never waits on Discord.

### 4.2 Topic catalog

Topics are stable strings, aligned with the existing activity kinds where possible. A catalog in Core supplies the category, default severity, a title, and a digest hint. A game/function can publish a known topic or a `custom.*` topic with its own title/body.

| Category | Topics (first set) | Default | Notes |
|---|---|---|---|
| `instance` | `instance.created`, `instance.installed`, `instance.running`, `instance.stopped`, `instance.crashed`, `instance.faulted`, `instance.deleted` | info / error | Crash and faulted are errors; deleted is info. |
| `job` | `job.succeeded`, `job.failed` | info / error | `job.failed` is on by default; success is opt-in. |
| `mods` | `mods.sync.failed`, `mods.update.available` | warning / info | Only the failures and a newer set, not every stage. |
| `update` | `update.available`, `update.installed`, `update.failed` | info / warning / error | `update.installed` comes from the Setup result after the restart. |
| `system` | `core.started`, `core.recovered`, `disk.low`, `network.changed` | info / warning | `core.recovered` means the previous stop was not clean. |
| `player` | `player.joined`, `player.left` | info | Off or digest by default; they are chatty. |
| `admin` | `admin.granted`, `admin.revoked` | info | Valheim writes the file; the notification is the audit trail. |
| `backup` | `backup.created`, `backup.failed`, `backup.restored` | info / error | Ready for the backup milestone; no dependency. |

A new game can use `player.joined` with its own fields (`Name`, `Platform ID`, `Server`) without touching Discord. A new function adds a topic to the catalog (or publishes `custom.*` with title/body). The catalog is the only place a topic's default behavior is described.

**Localization:** the catalog can hold `en` and `vi` templates for known topics; a destination chooses a language. A custom topic supplies one title/body (usually already in the event's language). First implementation may start English-only and add `vi` before the first release; the contract does not change.

### 4.3 Destinations and rules

`notifications.json` in the data root (atomic write, versioned like the other settings):

```json
{
  "schemaVersion": 1,
  "destinations": [
    {
      "id": "alerts",
      "name": "Alerts",
      "channel": "discord-webhook",
      "secret": "alerts",
      "enabled": true,
      "mode": "instant",
      "language": "vi",
      "minimumSeverity": "warning",
      "topics": ["instance.crashed", "instance.faulted", "job.failed", "update.available", "update.failed", "system.*"],
      "instances": ["*"],
      "mentionRoleId": null,
      "panelUrl": "http://100.96.2.89:8765"
    },
    {
      "id": "status",
      "name": "Server status",
      "channel": "discord-webhook",
      "secret": "status",
      "enabled": true,
      "mode": "status",
      "language": "vi",
      "topics": ["instance.*", "player.*"],
      "intervalSec": 60,
      "panelUrl": "http://100.96.2.89:8765"
    },
    {
      "id": "players",
      "name": "Player log",
      "channel": "discord-webhook",
      "secret": "players",
      "enabled": false,
      "mode": "digest",
      "language": "vi",
      "topics": ["player.*", "admin.*"],
      "digestSec": 300
    }
  ]
}
```

- `mode: instant` sends one message per event.
- `mode: digest` groups events in `digestSec` into one message (one embed per destination window; overflow becomes multiple messages).
- `mode: status` keeps one message per destination and edits it on change, at most every `intervalSec` (default 60 s). Webhooks cannot pin; the setup text says to pin it once by hand.
- `topics` supports exact names and one wildcard suffix (`instance.*`, `system.*`). A destination may filter by `instances`.
- `minimumSeverity` drops low-priority events without creating deliveries.
- `secret` names an entry in `ISecretStore` scope `notifications` (webhook URL). The URL never appears in the JSON, API, logs or the panel after save.
- A destination is disabled automatically after 404 (deleted webhook) or permanent 401/403; the panel shows the reason. Re-enabling is an explicit action after fixing the URL.

**Default routing:** crash, faulted, `job.failed`, `update.available`, `update.failed`, `disk.low` and `core.recovered` at `warning` or above; starts/stops and player events are opt-in. The Owner can change this in the panel.

### 4.4 Delivery tables

Two SQLite tables:

`notification_events` — one canonical event:
`id`, `createdAt`, `topic`, `severity`, `title`, `body`, `fieldsJson`, `instanceId`, `jobId`, `url`, `dedupeKey`, `source`, `expiresAt`.

`notification_deliveries` — one row per event × destination:
`id`, `eventId`, `destinationId`, `mode`, `state` (`pending` · `sending` · `sent` · `failed` · `dropped`), `attempts`, `nextAttemptAt`, `lastError`, `messageId`, `sentAt`.

Unique index `(eventId, destinationId)` makes fan-out idempotent. Events and successful deliveries are pruned after 7 days; failures after 30 days. The outbox is bounded (for example, 10,000 pending events) and stops accepting low-severity events first; critical events are never silently dropped without a panel line.

Why two tables rather than one queue: the same event can go to several destinations with different modes, and a destination failing must not duplicate an event to the others. The event is also the audit record the panel shows.

### 4.5 Dispatcher

`NotificationDispatcher` is a `BackgroundService`:

- claims due deliveries with a short SQLite transaction (one Core per machine, so a single consumer is enough);
- keeps **one in-flight request per destination**, so the channel's queue is ordered and rate limits are respected;
- asks the channel to send; writes `messageId`/`sentAt` or `lastError`/`nextAttemptAt`;
- retries transient failures (timeout, 5xx, 429) with exponential backoff and jitter; honors `Retry-After`/`retry_after`;
- treats 404 as permanent and disables the destination; 401/403 similarly after one attempt; 400 is dropped with the response body in the panel;
- coalesces digest deliveries by destination/window;
- updates status messages through `PATCH` when the `status` state changes or every `intervalSec`; skips the request when the rendered status is unchanged.

There is no hard-coded Discord rate limit. The channel reads the response headers and the dispatcher schedules the next attempt. Discord's explicit warning about 404 and invalid-request restrictions is part of the design.

## 5. The Discord adapter

`DiscordWebhookChannel` implements `INotificationChannel`:

- reads the webhook URL from `ISecretStore` by destination secret name;
- `POST ...?wait=true` for instant/digest; `PATCH .../messages/{id}?wait=true` for status;
- one embed per message (or one embed with fields for a digest), color by severity:
  - info `#3B82F6`, success `#22C55E`, warning `#F59E0B`, error `#EF4444`, critical `#991B1B`;
- title, body, fields (`Instance`, `Job`, `Player`, `Version`, …), footer `GameWarden <version> · <host>`, timestamp, optional panel link;
- `allowed_mentions` explicit:
  - default `{ "parse": [] }`;
  - a configured `mentionRoleId` adds `parse: ["roles"], roles: [id]` and the literal `<@&id>` only for critical events; no `@everyone` from game data;
- user-generated names and error text are sanitized: strip mention syntax, control characters and unsafe markdown links; truncate with `…`;
- content is not used for long text; embeds keep under 6000 characters and split if needed; digest splits into multiple messages at the 6000 limit;
- link buttons are optional and only open the panel (non-interactive); interactive buttons wait for a bot;
- `User-Agent: GameWarden/<version>`; timeout 10 s; `HttpClient` from the named client, no proxy.

**Status message** (one per status destination): a single embed with one field per instance:

```
Valheim · valheim-main   Running · 3/10 players · 32% CPU · 1.2 GB
Palworld · tvt-palworld  Running · 5/32 players · 41% CPU · 2.8 GB
```

It is edited only when the rendered content changes and at most every `intervalSec`; a webhook cannot pin, so the panel says *Pin this message once in Discord*. If the message was deleted, the next update creates a new one and stores the new id.

**What Discord cannot do without a bot:** buttons that start/stop servers, slash commands, a member list from a webhook, or reading channel history. Those are explicit non-goals for v1; a later bot can reuse `INotificationChannel` or add an interaction service, but it needs a public interactions endpoint or a Gateway connection and a new security review.

## 6. Panel and API

System → **Notifications** (Owner only, `system.manage`):

- destinations list with channel, enabled, mode, topics, last success/failure, *Test*, *Disable*;
- a topic/category matrix per destination, severity floor, instance filter and language;
- *Send test* calls the channel with a synthetic `system.test` event and shows the Discord result;
- a delivery log (newest 100) with topic, destination, state, attempts and redacted error;
- a status preview and the manual-pin note;
- the webhook URL is a write-only field; after save the API returns only `{ set: true }`.

API (a new generic surface; it supersedes the never-built `PUT /system/notifications/discord`):

| Method | Path | Notes |
|---|---|---|
| GET | `/system/notifications` | Destinations (URLs hidden), catalog, routing, last delivery summary |
| PUT | `/system/notifications` | Full document; validates channel, secret reference, topics, severity, modes |
| POST | `/system/notifications/destinations/{id}/test` | Sends `system.test`; returns `{ ok, messageId?, error? }` |
| GET | `/system/notifications/deliveries?limit=100` | Redacted delivery log; includes `nextAttemptAt` and `lastError` |
| POST | `/system/notifications/deliveries/{id}/retry` | Explicit retry for a failed delivery |

All mutations are activity-logged without the webhook URL. The URL is accepted only for `discord.com/api/webhooks/...` (and the documented `discordapp.com` alias); other hosts are rejected, so this cannot become an SSRF primitive.

## 7. Security and reliability

- **Secrets:** webhook URLs are credentials. Store them in `ISecretStore` under a new `notifications` scope; never in `notifications.json`, the database, activity, logs or API responses. The existing `AgentRedactor` already masks Discord webhook URLs; extend tests to the new paths.
- **Mentions:** default no mentions; role mention is explicit and only for `critical`; sanitize game/user text so a player name cannot inject `@everyone`.
- **SSRF:** fixed Discord host, fixed path from the stored URL, no user-supplied avatar/image URLs. Image avatars are not fetched by Core.
- **Permissions:** Owner session or loopback CLI (`system.manage`); no Agent permission by default. If an Agent needs notification management later, add an explicit `system.notifications` permission rather than reusing `system.manage`.
- **Failure isolation:** `Publish` never throws into the game/feature; a Discord outage is a delivery row, not a failed start/stop. The dispatcher catches and logs without the URL.
- **Durability:** outbox/deliveries survive a Core restart. On start, `sending` rows are returned to `pending` with a new attempt, so a crash does not strand them.
- **At-least-once:** a network timeout after Discord accepted a message may duplicate it; Discord has no idempotency key for webhooks. Duplicate risk is low; status edits are idempotent. Accepted and documented.
- **Invalid requests:** 404 disables immediately; 401/403 disables and alerts in the panel; 400 drops the message and stores the reason. Never hammer a dead webhook (Discord may temporarily restrict the IP).
- **Backpressure:** one in-flight per destination, bounded queues; low-priority events are dropped before critical ones, and the panel shows the drop count.
- **Privacy:** player names/platform IDs are already Owner-only; Discord destinations are chosen by the Owner. Do not send console lines, secrets, IPs or full command lines.
- **No inbound port:** webhook mode is outbound only. A bot with Gateway is also outbound but introduces a token and interaction surface; defer.

## 8. How a game or function adapts

**A game adapter/provider** publishes a topic with generic fields:

```csharp
notifications.Publish(new NotificationEvent(
    "player.joined",
    NotificationSeverity.Info,
    $"{player.Name} joined",
    Fields: [new("Server", server.Name), new("Platform ID", player.Id)],
    InstanceId: server.Id,
    DedupeKey: $"player.joined:{server.Id}:{player.Id}"));
```

No Discord code, no webhook, no embed. The same event reaches a future Slack adapter.

**A Core feature** either:

- emits an `ActivityRecord`/existing `CoreEvents` event and the bridge maps its kind; or
- publishes directly when the event is richer than the activity line (e.g. player/admin events).

**A new channel** implements `INotificationChannel` and registers one DI line. It gets the same event document and destination; it does not touch game code.

**A new notification kind** adds a topic to the catalog (category, default severity, localized title) and one `Publish` call. A `custom.*` topic with title/body works without a catalog entry.

## 9. Failure scenarios

| Scenario | Behavior |
|---|---|
| Webhook deleted | 404 → destination disabled, panel says why, no retries. |
| Token rotated/revoked | 401/403 → disabled after one try; panel asks for a new URL. |
| Discord down / timeout | Retry with exponential backoff (cap 1 h), keep the delivery; panel shows attempts. |
| 429 | Wait `retry_after`/`Retry-After`; no tight loop. |
| Message too long | Adapter truncates/splits; delivery succeeds. |
| Core restarts mid-send | `sending` returns to `pending`; possible duplicate for instant, status edits idempotent. |
| Outbox full | Low-severity/new events are rejected with a panel warning; critical events are kept. |
| Discord channel renamed/moved | Webhook keeps working; no action. |
| No network on the VM | Rows stay pending; panel shows the last error; no effect on games. |

## 10. Phases

1. **Foundation (one release):** `NotificationEvent`, publisher, topic catalog, rules/destinations, SQLite outbox/deliveries, one Discord webhook channel, instant embeds, test button, panel Settings page, secret storage, retries/rate limits. Events: instance crash/faulted/running/stopped, job failed/succeeded (success opt-in), update available/failed, core started/recovered, disk low.
2. **Signal over noise:** status message edit, digest mode for player/admin events, multiple destinations, delivery log + retry, per-destination language, panel URL links, mention role.
3. **Conversation (separate decision):** a Discord bot/gateway or interactions endpoint for buttons/slash commands, if the Owner wants actions from Discord. Reuses the event/channel model but is not a webhook feature.

The first release should not block on the bot or on backups; `backup.*` topics can be added by the backup milestone.

## 11. Acceptance criteria

- Killing a game process produces one Discord alert with the server name, exit reason and a panel link (when `panelUrl` is set), and the restart is not delayed by Discord.
- A failed job, a new release and a low-disk warning each produce a message once; a duplicate event with the same dedupe key in 10 minutes does not.
- A deleted webhook disables its destination and shows the reason in the panel; the panel's *Test* explains a bad URL without saving it.
- A 429 is honored and does not produce a burst of retries.
- A long error is truncated to Discord's limits; the message still sends.
- A status message edits in place; no new status message per minute; a deleted status message is recreated.
- The webhook URL never appears in logs, activity, API responses, the database or the panel after save.
- A game adapter can publish a player event with no Discord reference; a fake channel test receives the same event.

## 12. Decisions (chosen 2026-11-04)

1. **Webhook-only v1:** yes. Outbound only, no bot token, no public interactions endpoint. Buttons that call GameWarden wait for a later bot decision.
2. **Default events:** crash, faulted, job failed, update available/failed, disk low and core recovered. Starts/stops and player events opt-in; player events can be digest.
3. **Language:** per-destination `en`/`vi` with catalog templates, starting with Vietnamese for the group and English fallback.
4. **Destinations:** one alerts webhook plus one optional status webhook to start; the data model supports more.
5. **Panel URL:** optional per destination; no link when it is not reachable from Discord.
6. **Status message:** one message per destination, updated at most every 60 s, pinned by hand.
7. **Mentions:** none by default; optional role mention only for critical.

## 13. Companion: one queryable system log

Discord notifications are curated events, not a log firehose. Debugging needs the companion design in [19](19-system-log.md) and [ADR-0028](../decisions/0028-one-queryable-system-log.md): components write through `ILogger`, one `system_log` table is filterable by the Owner and the Agent, and notifications stay a separate high-value stream. The two share instance/job/request ids so a delivery can be traced back to the log.

## 14. Open questions / later

- A bot with Gateway (outbound) for slash commands and buttons; no public port but a new token/application surface.
- Discord OAuth login (OQ-04) stays separate and is not part of notifications.
- Notification digests across time zones/quiet hours; the VM uses one time zone, so later.
- Slack/Telegram: same channel interface; no work in v1.
- A public status page or `/healthz` remains separate from notifications.
