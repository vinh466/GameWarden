# ADR-0028: One queryable system log for the Owner and the Agent

- **Status:** Accepted (the Owner approved the direction on 2026-11-04). Design: [19](../docs/19-system-log.md). Implementation pending.
- **Date:** 2026-11-04

## Context

- When the Owner reports a problem, the Agent currently reads several files through `AgentLogService` (core, job, instance, setup) and cannot join them by component, instance, job or request.
- Core's `FileLoggerProvider` is the only complete Core log; `ActivityLog` is deliberately curated and `events` is not a diagnostic store.
- The Owner asked for one common log that both they and the Agent can filter, and for components to write into it without their own UI or database.
- Logs can contain secrets, tokens and webhook URLs; the Agent redactor already masks known patterns but applies at read time.
- A new SQLite table and a logger provider must not put the game/request path at risk if the database is slow or unavailable.

## Decision

1. **One structured `system_log` table** with time, level, component/category, message, exception, instance, job, request, Agent client, source and redacted JSON data. Indexed for the common filters; newest first with a cursor.
2. **Components write through `ILogger`** with scopes (`instanceId`, `jobId`, `requestId`); a `SystemLogLoggerProvider` captures them. An `ISystemLog` writer exists for structured records that are not a formatted log line.
3. **Redact before insert** with the same sanitizer as the Agent reader. Raw secrets never reach SQLite. Console lines are not mirrored by default.
4. **One query service** for the Owner API (`/system/logs`) and the Agent API (`/agent/logs?source=system`). The Agent needs `agent.logs.read` plus the diagnostics window; the Owner needs `system.read`. The panel gets a filterable Logs page.
5. **Core first, Runner/Setup later.** Core logs are the initial source; Runner and Setup ingestion follows without changing the API.
6. **File logs stay** as the boot/emergency copy. The system log is a bounded queue + batched SQLite inserts; if it is unavailable, records fall back to the file and the game/request path continues.
7. **Retention:** 14 days or 250,000 rows, whichever comes first; warnings/errors are kept over debug/info when pruning. Default stored floor is `Information`; debug/trace are opt-in.
8. **Notifications are separate.** The system log is not a Discord firehose; curated `NotificationEvent`s ([ADR-0027](0027-notifications-through-a-provider-neutral-outbox.md)) drive Discord.

## Consequences

- The Owner and the Agent share one filterable source; the Agent can triage by summary, component, instance and job without guessing a file.
- The log adds a write path to SQLite and a migration; a full/broken database falls back to files and never stops a server.
- Storing logs has privacy risk; redaction at write, Owner/Agent authorization and retention are mandatory.
- Runner/Setup/game-console sources arrive later; until then the existing file endpoints remain the deep tails.
- A future MCP tool can expose the same filters without a new backend.

## Alternatives considered

- **Keep only text files** — no cross-component query or joins; the Agent has to read and parse several tails. Rejected.
- **Use `ActivityLog` as the system log** — it is curated, small and user-facing; mixing debug noise would ruin the feed. Rejected.
- **Send logs to Discord** — noisy and a privacy leak. Rejected; notifications are curated.
- **Write one shared file from Core, Runner and Setup** — multi-process locking, rotation and no structured query. Rejected.
- **A full OpenTelemetry stack** — too much for one VM and one Owner; a small SQLite log is enough. Revisit if the project grows.
