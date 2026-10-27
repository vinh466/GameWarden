# 05 · API

The panel and the CLI use the same API. Nothing the panel does is unavailable to the API.

## 1. Conventions

| Topic | Rule |
|---|---|
| Base path | `/api/v1` |
| Format | JSON, camelCase, ISO-8601 UTC timestamps |
| Auth (panel) | Session cookie (`HttpOnly`, `SameSite=Strict`) set by `POST /auth/login` |
| Auth (CLI) | `Authorization: Bearer <local token>`, accepted **only** on loopback |
| CSRF | State-changing requests from the panel MUST send header `X-GW-CSRF` matching the token returned at login |
| Errors | RFC 7807 ProblemDetails with a stable `code` (§5) |
| Long operations | Return `202 Accepted` with `{ "jobId" }` and `Location: /api/v1/jobs/{jobId}` |
| Authorization | Every endpoint declares one permission; instance endpoints check it against the instance scope. MVP: the only account is the Owner, who holds every permission; the names stay so roles can be added later ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)) |
| Host header | Only configured bind addresses and `localhost` are accepted (ASP.NET Core host filtering), against DNS rebinding (OQ-22) |
| Lists | `?limit=&cursor=` where lists can grow (jobs, audit, backups) |

## 2. Endpoints

### Auth and session

| Method | Path | Permission | Notes |
|---|---|---|---|
| POST | `/auth/login` | – | `{ username, password }` → sets cookie, returns `{ user, permissions, csrfToken }`. Rate-limited. |
| POST | `/auth/logout` | – | |
| GET | `/auth/me` | – | Current user, effective permissions per scope |
| POST | `/auth/invites/{token}/accept` | – | `{ username, password }` creates the account (after MVP) |
| POST | `/auth/password` | – | Change own password |

### Setup (first run only)

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/setup/status` | – | `{ needsSetup }` |
| POST | `/setup/owner` | – | `{ setupToken, username, password }`. Only while no user exists, **only from loopback**, and only with the one-time setup token that the installer puts in the setup URL it opens (also printed by `gamewarden setup-url`) (OQ-22) |

### System (Owner)

| Method | Path | Permission |
|---|---|---|
| GET | `/system` | `system.read` — version, uptime, CPU/RAM/disk, bind addresses, VPN interfaces detected |
| PUT | `/system/settings` | `system.manage` — bind addresses, VPN CIDRs for firewall, backup defaults |
| GET | `/system/prerequisites` | `system.read` |
| POST | `/system/prerequisites/{id}/install` | `system.manage` → job |
| PUT | `/system/notifications/discord` | `system.manage` — webhook (secret), event selection |
| POST | `/system/notifications/discord/test` | `system.manage` |

### Users and access (Owner; after MVP, M6)

| Method | Path | Permission |
|---|---|---|
| GET | `/users` | `users.manage` |
| PATCH | `/users/{id}` | `users.manage` — disable, reset password |
| PUT | `/users/{id}/roles` | `users.manage` — replace role bindings |
| POST | `/invites` | `users.manage` — `{ role, scope, expiresInHours }` → `{ url }` |
| DELETE | `/invites/{token}` | `users.manage` |
| GET | `/audit` | `audit.read` (Owner only) |

### Adapters

| Method | Path | Permission |
|---|---|---|
| GET | `/adapters` | `adapters.read` — id, name, level, version, validation errors |
| GET | `/adapters/{id}` | `adapters.read` — schema, ports, resources, guide |
| POST | `/adapters/reload` | `adapters.manage` |

### Instances

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/instances` | `instance.view` | Filtered to instances the user can view |
| POST | `/instances` | `instances.create` | `{ id, adapterId, displayName, settings, ports? }` → instance + install job |
| GET | `/instances/{id}` | `instance.view` | |
| GET | `/instances/{id}/connect-info` | `instance.connectInfo` | Text for the Share button (address, port, password if allowed, required mods) |
| POST | `/instances/{id}/start` · `/stop` · `/restart` | `instance.control` | `start` / `restart` accept `?force=true` (Owner only) to skip the minimum-resource check |
| POST | `/instances/{id}/install` · `/update` | `instance.update` | → job |
| POST | `/instances/{id}/update-check` | `instance.update` | → job; it takes the global SteamCMD lock (OQ-22) |
| GET | `/instances/{id}/settings` | `instance.settings.read` | Secrets returned as `{ "set": true }` |
| PUT | `/instances/{id}/settings` | `instance.settings.write` | Validated by schema; response says if restart is required |
| GET | `/instances/{id}/console?tail=200` | `instance.console.read` | |
| POST | `/instances/{id}/console` | `instance.console.write` | `{ command }`; allowlist enforced unless `instance.console.writeAny` |
| GET/POST | `/instances/{id}/backups` | `instance.details.read` / `instance.backup.create` | POST → job |
| POST | `/instances/{id}/backups/{backupId}/restore` | `instance.backup.restore` | → job (includes pre-restore backup) |
| GET | `/instances/{id}/backups/{backupId}/download` | `instance.backup.restore` | |
| GET/PUT | `/instances/{id}/mods` | `instance.details.read` / `instance.mods.manage` | PUT → `mods.sync` job |
| GET/PUT | `/instances/{id}/schedules` | `instance.details.read` / `instance.schedule.manage` | |
| DELETE | `/instances/{id}?mode=archive\|purge` | `instance.delete` | → job; `purge` requires `confirm={id}` |

### Jobs and activity

| Method | Path | Permission |
|---|---|---|
| GET | `/jobs?instanceId=&state=` | `instance.details.read` (filtered) |
| GET | `/jobs/{id}` | `instance.details.read` on its instance, or `system.read` for system jobs |
| GET | `/jobs/{id}/log` | same |
| POST | `/jobs/{id}/cancel` | the permission that created that job type |
| GET | `/activity` | `instance.view` (filtered) — user-friendly feed built from events + audit |

### Unauthenticated

| Method | Path | Notes |
|---|---|---|
| GET | `/healthz` | `{ "status": "ok" }` only. No version, no instance data. |

## 3. Realtime (SignalR)

Hub: `/hubs/events`. The hub accepts a connection only when its `Origin` is an allowed host (OQ-22). On connect, the server places the connection in groups based on the user's permissions.

| Client → server | Permission |
|---|---|
| `SubscribeConsole(instanceId)` | `instance.console.read` |
| `UnsubscribeConsole(instanceId)` | – |

| Server → client | Payload |
|---|---|
| `InstanceStateChanged` | `{ instanceId, state, desiredState, activeJobId?, at }` |
| `PlayersChanged` | `{ instanceId, count, max }` |
| `JobUpdated` | `{ jobId, instanceId?, state, progress, stage }` |
| `ConsoleLines` | `{ instanceId, lines: [{ at, stream, text }] }` (batched every ~100 ms) |
| `ActivityAdded` | Activity item |
| `CoreStatus` | `{ version, uptimeSec }` heartbeat every 15 s |

Clients reconnect automatically; after reconnect they re-fetch state with REST (events are not replayed).

## 4. Example: start an instance

```http
POST /api/v1/instances/valheim-main/start
X-GW-CSRF: 6f1c...

202 Accepted
{ "instanceId": "valheim-main", "state": "Starting", "desiredState": "running" }
```

## 5. Error codes

| HTTP | `code` | When |
|---|---|---|
| 400 | `validation_failed` | Body or settings fail validation (`errors` map included) |
| 401 | `unauthenticated` | No/expired session |
| 403 | `forbidden` | Missing permission for this scope |
| 404 | `not_found` | Unknown or not visible to the user (same response, to avoid leaking ids) |
| 409 | `instance_busy` | An exclusive job holds the instance lock (`jobId` included) |
| 409 | `invalid_state` | e.g. start while `NotInstalled` |
| 409 | `port_conflict` | Port used by another instance or process |
| 412 | `prerequisite_missing` | e.g. VC++ runtime missing (`prerequisites` included) |
| 412 | `resources_insufficient` | Below the adapter's `resources.min` (`required` and `available` included); the Owner may retry with `force=true` |
| 429 | `rate_limited` | Login throttling |
