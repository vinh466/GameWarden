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
| POST | `/auth/login` | – | `{ userName, password }` → sets the session cookie, returns `{ userName, isOwner, csrfToken }` (MVP: the Owner holds every permission, so no permission list yet). Five failures lock the name for 15 min; 10 attempts a minute per address. |
| POST | `/auth/logout` | – | |
| GET | `/auth/me` | – | The signed-in user and a CSRF token, like `/auth/login`; 401 when signed out |
| POST | `/auth/invites/{token}/accept` | – | `{ username, password }` creates the account (after MVP) |
| POST | `/auth/password` | – | `{ currentPassword, newPassword }`: change own password; every other session of the user ends, this one gets a new cookie and CSRF token |

### Setup (first run only)

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/setup/status` | – | `{ needsSetup }` |
| POST | `/setup/owner` | – | `{ setupToken, username, password }`. Only while no user exists, **only from loopback**, and only with the one-time setup token that the installer puts in the setup URL it opens (also printed by `gamewarden setup-url`) (OQ-22). Signs the Owner in. |
| GET | `/setup/url` | CLI only | The setup link with the token while no account exists (`gamewarden setup-url`); 409 `already_set_up` afterwards |
| POST | `/owner/password` | CLI only | `{ password }`: `gamewarden owner reset-password`; ends every session of the Owner |

### System (Owner)

| Method | Path | Permission |
|---|---|---|
| GET | `/system` | `system.read` — version, uptime, CPU/RAM/disk, bind addresses, VPN interfaces detected |
| GET / PUT | `/system/settings` | `system.manage` — `{ panelAccessNetworks, vpnRanges }` (empty = the default, the VPN ranges); written to `gamewarden.json` and applied at once (address filter, panel and game-port firewall rules). 409 `would_lock_out` when the browser making the change would no longer reach the panel. Backup defaults come with backups (backlog, ADR-0018) |
| GET | `/system/versions` | `system.read` — `{ current, installed, previous, canRestoreDatabase }` |
| POST | `/system/versions/switch` | `system.manage` — `{ version, restoreDatabase? }` → 202: that version's Setup switches the service to it, like `gamewarden version use` |
| GET | `/system/prerequisites` | `system.read` |
| GET | `/metrics` | `system.read` — CPU (share of the whole machine) and RAM of the VM and of each running game's process tree: `{ machine, instances: { <id>: … } }`, each `{ latest, history }` with ten minutes of 5 s samples |
| GET | `/system/update?prerelease=` | `system.read` — running and latest version from GitHub Releases ([10 §6](10-build-test-release.md#6-self-update-from-github-releases)) |
| POST | `/system/update` | `system.manage` — `{ prerelease?, version?, fromZip?, allowUnsigned? }` → 202 `{ from, to }`; Core verifies and prepares the package, then the new version's Setup restarts the service |
| POST | `/system/uninstall-cleanup` | CLI only, called by Setup's uninstall: stops every instance, removes the firewall rules and the `gamewarden-run` user |
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
| GET | `/adapters` | `adapters.read` — id, name, level, version, ports, resources, validation errors; `settingGroups` and `settings`: the form of [04 §5](04-adapter-spec.md#5-settingsschemajson-l2) with defaults as text and `required` / `wizard` worked out (an L1 adapter gets one group of its required settings and secrets); `hasHelp` on each setting and group that has a topic in the adapter's `help.json` |
| GET | `/adapters/{id}/help` | `adapters.read` — `{ topics: { key: topic } }`, the adapter's `help.json` ([04 §5.3](04-adapter-spec.md#53-helpjson)) with absent fields as `null`; `{ topics: {} }` when it has none; 404 `not_found` for an unknown adapter. Fetched when the panel opens a help drawer ([12 §5.1](12-explanations.md#51-where-the-text-lives)) |
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
| DELETE | `/instances/{id}?mode=archive\|purge` | `instance.delete` | → job; `purge` requires `confirm={id}`. M1 has `purge` only, done in the request; `archive` comes with backups (backlog, ADR-0018) |

### Jobs and activity

| Method | Path | Permission |
|---|---|---|
| GET | `/jobs?instanceId=&state=` | `instance.details.read` (filtered) |
| GET | `/jobs/{id}` | `instance.details.read` on its instance, or `system.read` for system jobs |
| GET | `/jobs/{id}/log` | same |
| POST | `/jobs/{id}/cancel` | the permission that created that job type |
| GET | `/activity?instanceId=&before=&limit=` | `instance.view` (filtered) — the feed, newest first (`before`: an item id, for older pages; `limit` ≤ 200): instance moments (created, installed, running, stopped, crashed, faulted, deleted), finished jobs, commands with the user who sent them (console text left out), updates and Core starts. Kept: the newest 10 000. |

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

| Server → client | Arguments |
|---|---|
| `InstanceChanged` | The instance as `GET /instances/{id}` returns it, on creation and whenever its state, desired state, job lock, problem or restart count changes |
| `InstanceRemoved` | `instanceId` |
| `JobUpdated` | The job as `GET /jobs/{id}` returns it: created, each stage, progress (every 2 s), finished |
| `ConsoleLines` | `instanceId`, `lines: [{ at, stream, text }]` — batched every 100 ms, at most 500 lines a batch; only to subscribers of that console |
| `ActivityAdded` | The item as `GET /activity` returns it |
| `CoreStatus` | `version`, `uptimeSec` — heartbeat every 15 s |
| `MetricsSampled` | `machine`, `instances: { <id>: point }` — every 5 s, one `{ at, cpuPercent, ramMb }` per running server; a server missing from it has stopped |
| `PlayersChanged` | `{ instanceId, count, max }` — with the player probe (later) |

Messages carry the REST API's own types, so the panel's generated client types describe them. Implemented 2026-10-28 (M2 phase 3).

Clients reconnect automatically; after reconnect they re-fetch state with REST (events are not replayed). The hub refuses a connection whose `Origin` is not the panel's own host (403).

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
| 400 / 401 | `invalid_credentials` | Wrong user name or password (401 at sign-in, 400 for the current password when changing it) |
| 403 | `csrf_failed` | A change made with the session cookie lacks the `X-GW-CSRF` token |
| 403 | `setup_loopback_only` | Creating the Owner from anywhere but the machine itself |
| 403 | `invalid_setup_token` | Wrong or used setup token |
| 409 | `already_set_up` | Setup after the Owner exists |
| 409 | `would_lock_out` | A settings change that would shut out the browser making it |
| 429 | `rate_limited` | Login throttling |
