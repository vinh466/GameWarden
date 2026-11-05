# ADR-0032: Notifications go through provider-neutral events and one durable outbox; Discord is a channel adapter

- **Status:** Accepted (the Owner approved the direction on 2026-11-03). Built on 2026-11-03 in #49 (instant) and #53 (digest, status); what is not built yet is listed in [18](../docs/18-discord-notifications.md).
- **Date:** 2026-11-03
- **Number:** written as ADR-0027 on 2026-11-03; renumbered on 2026-11-05 because [ADR-0027](0027-development-releases-move-forward.md) is the release decision.

## Context

- The backlog promises Discord notifications for crashes, updates, jobs and a live status message ([ADR-0019](0019-discord-joins-the-backlog-mods-next.md), [18](../docs/18-discord-notifications.md)).
- Features and games already produce events, but Discord must not become a dependency of every feature or adapter. A future Slack/Telegram channel should not require another event pipeline.
- Discord webhooks are one-way, have strict message/embed limits, rate limits and 429/404 failure modes; a status message can be edited but not pinned by a webhook. Interactive buttons need a bot/application, not a webhook.
- Webhook URLs are credentials and must stay out of the database, logs and panel responses.

## Decision

1. **One notification event contract**, independent of the channel: topic, severity, title, body, fields, instance/job, URL, dedupe key and structured data.
2. **One durable outbox**: `notification_events` plus `notification_deliveries` in SQLite; one delivery per event × destination, retried after a Core restart. No in-memory queue is the source of truth.
3. **Routing is data**: `notifications.json` holds destinations, topic filters, minimum severity, instance filter, mode (`instant`, `digest`, `status`), language and optional role mention. Webhook URLs live in `ISecretStore` under a `notifications` scope.
4. **Discord is one `INotificationChannel` adapter**, `DiscordWebhookChannel`, and owns embeds, truncation, allowed mentions, `Retry-After`, 429/404 handling and status edits. It is webhook-only in v1; interactive components/bots are a later decision.
5. **Games and features publish topics**, e.g. `instance.crashed`, `job.failed`, `player.joined`; no Discord types appear outside the adapter. A new channel is one registration; a new topic is a catalog entry plus a `Publish` call.
6. **Delivery is at-least-once and observable**: a delivery log in the panel, disabled destinations on 404/401/403, bounded queues and explicit low-priority drops.
7. **One live status message per status destination**, edited in place at most every 60 s; pinned manually because webhooks cannot pin.

## Consequences

- Discord can be enabled without touching game code or the instance state machine; a Discord outage never fails a start/stop/job.
- The owner sees a stable, filterable event history; the panel and future channels share it.
- The outbox adds two tables and a background dispatcher; a rare duplicate is possible after a timeout (no webhook idempotency key).
- A deleted webhook disables its destination until the Owner fixes it; the panel says why.
- Bot features (buttons, slash commands, actions from Discord) are explicitly deferred.

## Alternatives considered

- **Call Discord directly from every feature** — couples every feature to Discord and breaks on a rate limit or outage. Rejected.
- **One webhook URL in `gamewarden.json`** — simpler, but one channel and no durable retries or delivery history. Rejected.
- **A bot first** — needs a public interactions endpoint or Gateway, a bot token and a new security surface. Webhook is enough for one-way notifications. Deferred.
- **A raw log stream to Discord** — noisy, expensive and leaks too much. Notifications stay curated; the system log is separate ([ADR-0028](0028-one-queryable-system-log.md)).
