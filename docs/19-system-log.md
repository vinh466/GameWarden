# 19 · One system log for the Owner and the Agent

**Status: Accepted (2026-11-04); implementation pending.** Addendum to the notification platform in [18](18-discord-notifications.md). Agent access is [15](15-agent-access.md); Core logs and operations are [08](08-operations.md#5-logs); the event/activity model is [03](03-domain-model.md#6-events).

## 1. Goal

Give the Owner and the Agent one place to ask **what went wrong**, with filters, instead of tailing four kinds of files and guessing which one matters:

- every Core component writes structured log records through `ILogger`; no component needs its own UI or database;
- one query surface filters by time, level, component, instance, job, request and text;
- the Agent can read the same records when the Owner reports a problem, and can summarise the last hour before opening a console or a file;
- notifications ([18](18-discord-notifications.md)) stay curated events, not a firehose of log lines;
- the existing file logs stay as the boot/fallback copy; the system log is the queryable copy.

Non-goals for the first version: a full APM/tracing product, shipping every console line or debug message to SQLite, and a public log endpoint.

## 2. What exists today

| Source | What it is | Limitation |
|---|---|---|
| `FileLoggerProvider` | Core's daily text logs in `logs/core-*.log` | Human-readable lines; no filters or joins; a 4 MB tail cap in the Agent reader |
| `ActivityLog` | Curated `events` rows for instance/job/user actions | Deliberately small; not a diagnostic log; no stack traces |
| `AgentLogService` | Tails core, job, instance and setup files, redacts and filters `level`/`contains` | File-based, per-source; no structured fields or cross-component query |
| Job logs, instance console, Setup logs | Per-workload text | Each has its own endpoint/reader |
| `AgentRedactor` | Masks known secrets, tokens and Discord webhook URLs | Applied at read time today; the system log must apply it before storing |

The pieces already exist; the missing piece is a **structured, indexed, redacted record** with one query API.

## 3. Record model

SQLite table `system_log`:

| Column | Type | Notes |
|---|---|---|
| `Id` | long PK | Monotonic; the query cursor |
| `At` | timestamp | UTC |
| `Level` | string | `trace`, `debug`, `info`, `warn`, `error`, `critical` |
| `Component` | string | `GameWarden.Core.Supervision.Supervisor`, `discord`, `integration.valheim`, … |
| `Category` | string | `ILogger` category; `Component` is the stable short name |
| `EventId` | int? | .NET `EventId` |
| `Message` | string (4000) | Rendered message, redacted |
| `Exception` | string? (8000) | Type + message + stack, redacted |
| `InstanceId` | string? | From a log scope or explicit write |
| `JobId` | string? | From a log scope |
| `RequestId` | string? | HTTP/Agent request id when present |
| `AgentClientId` | string? | When an Agent action logs something |
| `Source` | string | `core`, `runner`, `setup`, `game`, `notification`, `integration` |
| `Data` | JSON? (8000) | Structured fields from scopes, redacted. Keys only where the value is safe |

Indexes: `(At)`, `(Level, At)`, `(Component, At)`, `(InstanceId, At)`, `(JobId, At)`, and `(RequestId)`. A text search uses `LIKE` in v1; if it becomes slow, an FTS5 virtual table is added without changing the API.

The **level mapping** is the usual .NET one (`Trace`/`Debug`/`Information`/`Warning`/`Error`/`Critical`). The default stored floor is `Information`; `Debug`/`Trace` are opt-in with a configuration value. Warnings and errors are always stored.

## 4. How components feed it

```csharp
// Any Core component: nothing new to call.
logger.LogInformation("Job {JobId} reached {Stage}.", job.Id, job.Stage);

// Structured context for a whole unit of work.
using (logger.BeginScope(new Dictionary<string, object?>
       {
           ["instanceId"] = instanceId,
           ["jobId"] = jobId,
           ["requestId"] = http.TraceIdentifier,
       }))
{
    logger.LogError(ex, "The update failed.");
}
```

- A new `SystemLogLoggerProvider` is registered next to `FileLoggerProvider`; it captures every Core log, extracts the scopes, redacts and enqueues a record.
- `ILogger` is enough for "components just dump into it"; no per-component service or table.
- A small `ISystemLog`/`SystemLogWriter` is available for records that are not a log message (e.g. a game integration wants a structured `player.joined` row). It writes the same table.
- The provider **never logs its own failure into itself**. If SQLite is unavailable, records go to a bounded fallback queue and then to the file log; the game and the request path keep running.
- The FileLogger stays: it is the only copy before the database is migrated and the emergency copy if the database is corrupt.

### 4.1 Sources outside Core

- **Runner**: phase 2. The Runner already has a pipe to Core; add a `diagnostic` event (or a small structured line) so its warnings and exits land in the same table. Until then, `AgentLogService` still reads `logs/runner/runner.log` and the instance console.
- **Setup**: phase 2. Setup writes its result file; Core logs it on start. A dedicated setup ingestion can follow.
- **Game console**: not every line. The console remains its own tail; `stderr` lines and adapter-declared error patterns can be mirrored later as `source=game`.
- **Jobs**: job logs remain their own detailed file; job start/finish/error is already logged through `JobService` and becomes a system-log row. The job log stays the deep tail.

## 5. Query API

Owner (session/CLI, `system.read`):

| Method | Path | Notes |
|---|---|---|
| GET | `/system/logs?from=&to=&level=&component=&instanceId=&jobId=&q=&before=&limit=` | Newest first; `limit` 1–500 (default 100); `before` is the cursor; `level` may repeat or be comma-separated |
| GET | `/system/logs/summary?minutes=60` | Counts by level and component, newest error, newest critical, top recurring message fingerprints |
| GET | `/system/logs/components` | Known components/categories and their last timestamp |

Agent (token, `agent.logs.read` + diagnostics window, because this includes Core internals):

| Method | Path | Notes |
|---|---|---|
| GET | `/agent/logs?source=system&...` | The same filters, redacted; reuses the existing Agent log shape so MCP tools and the CLI do not need a second parser |
| GET | `/agent/logs/summary?minutes=60` | The same summary for a quick triage |

The query layer is one service (`SystemLogQuery`) used by both endpoints. The Owner and the Agent see the same records; only the authentication and diagnostics window differ.

The panel's System → **Logs** page is a virtualised table with level/component/instance/job/time filters, a search box, pause/live toggle and a detail drawer for the exception/`data`. It uses the generated API client like every other page. The console tab remains the game's console; the system log is not a replacement.

## 6. Redaction and privacy

- Redact **before insert**, not only at read: `AgentRedactor` (or a shared `LogSanitizer` extracted from it) masks server passwords, admin passwords, tokens, cookies, Discord webhook URLs and common URL credentials. The raw value never reaches SQLite.
- Exception text and `Data` pass through the same redactor. A stack trace is kept; a connection string, token or webhook is not.
- The system log does not store console lines by default. Console lines can contain chat, IPs and passwords; a future game-console mirror must use an explicit adapter mapping and redaction.
- The Owner route requires `system.read`; the Agent route requires `agent.logs.read` **and** the diagnostics window (the same rule as today's core logs). No Agent token gets it permanently.
- Agent reads are audited like every Agent request (existing `AgentAudit`); the audit records the filter, not the returned lines.
- Retention: 14 days or 250,000 rows, whichever comes first. A warning/error count is kept in the summary so pruning is visible. No export by default; the Agent can page.

## 7. Reliability

- A bounded `Channel<SystemLogRecord>` (e.g. 20,000 records) with batched inserts every 250 ms or 100 records; a full channel drops `debug`/`trace` first, then `info`, and increments a dropped counter. Warnings/errors are never dropped silently.
- The logger provider is synchronous from the caller's view (enqueue only), so an HTTP request or a state transition never waits for SQLite.
- `ILogger` recursion is prevented: the provider's own errors go to `FileLogger`/stderr, not through `ILogger`.
- On Core start, migrate before starting the provider; before that, logs go to the file only.
- Pruning runs at startup and hourly; a failed prune is logged, not fatal.
- The system log never becomes a notification source by itself; a feature publishes a `NotificationEvent` ([18](18-discord-notifications.md)). This keeps Discord quiet and the log complete.

## 8. Debug workflow for the Agent

When the Owner reports a problem:

1. `GET /agent/logs/summary?minutes=60` → which component, level and message fingerprint is spiking.
2. `GET /agent/logs?source=system&level=warn,error&instanceId=…&since=…&limit=200` → the exact records; or `q=` for the error text.
3. `GET /agent/logs?source=system&jobId=…` → the job's Core-side story; the job log has the tool output.
4. `GET /agent/logs?source=instance&id=…` → the game console tail, if needed.
5. If the issue is a known event, the same `topic`/`dedupeKey` in the notification delivery log shows whether Discord was told; the notification platform is not used as a log store.

A future MCP tool `gamewarden_system_log` (or an extension of `gamewarden_agent_get`) exposes the same filters; until then `/agent/logs?source=system` works through the generic Agent endpoint.

## 9. API examples

```http
GET /api/v1/system/logs?from=2026-11-04T00:00:00Z&level=warn,error&component=GameWarden.Core.Supervision&limit=50
GET /api/v1/system/logs?instanceId=valheim-main&jobId=20261104-101500-ab12&before=918233
GET /api/v1/system/logs/summary?minutes=60
```

```json
{
  "items": [
    {
      "id": 918233,
      "at": "2026-11-04T10:15:03.412Z",
      "level": "error",
      "component": "GameWarden.Core.Jobs.JobService",
      "category": "GameWarden.Core.Jobs.JobService",
      "message": "Job 20261104-101500-ab12 failed: SteamCMD exited 8.",
      "exception": null,
      "instanceId": "valheim-main",
      "jobId": "20261104-101500-ab12",
      "requestId": null,
      "source": "core",
      "data": { "stage": "install" }
    }
  ],
  "nextBefore": 918232
}
```

## 10. Tests

- Unit: level mapping, scope extraction, redaction before insert, message/exception truncation, query filters and cursor, retention/prune, dropped-counter behavior.
- Integration: a Core log line reaches SQLite and the Owner API; an Agent with diagnostics sees it; without the window it gets 403; a secret value is absent after redaction; a full queue drops debug first.
- Panel: filter by level/instance/job/text, live pause, detail drawer.
- Agent: `source=system` returns the same record as the Owner API; audit has the filter.

## 11. Phases and decisions (chosen 2026-11-04)

1. **Foundation:** table + migration, logger provider, `SystemLogQuery`, Owner/Agent API, panel Logs page, retention, redaction. Core sources only.
2. **Closer monitoring:** Runner/Setup ingestion, game `stderr`/known error patterns, FTS5 if search needs it, live SignalR tail, MCP tool.
3. **Optional:** metrics/log correlation and an error fingerprint dashboard; no change to notification semantics.

Chosen defaults:

1. Retention: 14 days or 250k rows.
2. Stored floor: `Information`; debug/trace opt-in.
3. Agent access: `agent.logs.read` plus the diagnostics window; no permanent Agent log permission.
4. Core first; Runner/Setup in phase 2.
5. Owner-only panel Logs page in v1; no friend-facing view.
