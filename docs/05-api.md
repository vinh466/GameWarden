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
| GET | `/adapters` | `adapters.read` — id, name, level, version, ports, resources, validation errors; `settingGroups` and `settings`: the form of [04 §5](04-adapter-spec.md#5-settingsschemajson-l2) with defaults as text and `required` / `wizard` worked out (an L1 adapter gets one group of its required settings and secrets); `hasHelp` on each setting and group that has a topic in the adapter's `help.json`; `modLoader`: the loader of an L3 adapter's `mods`, `null` without mods |
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
| GET | `/instances/{id}/mods` | `instance.details.read` | `{ instanceId, loader, enabled, active, previous, pending, shareCode, shareCodeCreatedAt, newerVersions, community, runningSetId, restartRequired }`: the active set (used at the next start), the previous one, and the newest set still `building` or `failed` with its `jobId`; each set has its `origin` (`edit` or `code`) and its `entries` (`package`, `version`, `enabled`, `origin`: `chosen` or `dependency`). `shareCode` is the last code made for friends; `newerVersions` is the last update check (`checkedAt`, `updates`, `states`), kept in memory until Core restarts and showing only what is still newer than the active set. `community` is the Thunderstore community of the adapter's source; `runningSetId` the set the last start with *Mods on* loaded; `restartRequired` is true while the server runs and its next start would load something else: *Mods on* with a ready active set other than the running one, or *Mods on* / *off* other than at its last start (phase 2). 409 `invalid_state` when the adapter has no `mods` ([13 §4.11](13-mods.md#411-api-and-cli); implemented 2026-10-30, phases 1a, 1c and 2) |
| PUT | `/instances/{id}/mods` | `instance.mods.manage` | One of two bodies → 202 and a `mods.sync` job, which does not stop the server. `{ entries: [{ package, version?, enabled }] }`: the whole list of chosen mods (`version` left out = the latest; Core adds the dependencies); 400 `validation_failed` for a name that is not `Namespace-Name`, a version that is not `x.y.z` or a package listed twice. `{ previewId }`: a code preview as it was shown (phase 1c): its entries exactly, versions and `enabled` included, origin `code`, and the code's config files over the set's configs when it is built ([13 §4.4](13-mods.md#44-getting-a-mod-set), [§4.7](13-mods.md#47-configs)); 404 `not_found` when the preview is unknown, expired or made for another instance. 400 for both bodies or neither; 409 `mods_busy` while a mods job runs |
| POST | `/instances/{id}/mods/code-preview` | `instance.mods.manage` | `{ code }`, an r2modman profile code (a UUID; case and spaces around it do not matter) → 200 `{ previewId, code, profileName, expiresAt, entries: [{ package, version, enabled, state }], warnings, configFiles }`; `state` is Thunderstore's: `ok`, `deprecated` or `notFound`; `warnings` name the mods and files left out; `configFiles` are paths under `BepInEx/config`. Changes nothing; the preview lasts 30 minutes. 400 `validation_failed` for text that is not a code or a code Core refuses (not r2modman's format, over the limits, an entry that leaves its folder); 404 `not_found` for a code that is unknown or expired; 502 `upstream_unavailable` when Thunderstore does not answer or keeps answering 429 (phase 1c) |
| POST | `/instances/{id}/mods/share-code` | `instance.mods.manage` | An r2modman code of the active set's enabled mods, named after the instance, without config files ([13 §4.6](13-mods.md#46-sharing-with-friends)) → 200 `{ code, createdAt }`, also stored for `GET`; 409 `invalid_state` without an active set with mods; 502 `upstream_unavailable` (Thunderstore makes six codes a minute at most) |
| POST | `/instances/{id}/mods/check-updates` | `instance.mods.manage` | The latest version of each enabled package of the active set, at most four requests at a time ([13 §4.8](13-mods.md#48-updates)) → 200 `{ checkedAt, updates: [{ package, current, latest }], states: [{ package, state }] }` (`state`: `ok`, `deprecated` or `notFound`, phase 2); nothing changes (updating is a `PUT` with the new versions); 409 `invalid_state` without an active set; 502 `upstream_unavailable` |
| GET | `/instances/{id}/mods/search?q=&limit=` | `instance.mods.manage` | Thunderstore search in the community of the adapter's source ([13 §4.4](13-mods.md#44-getting-a-mod-set) item 3, phase 2) → 200 `{ community, fetchedAt, results: [{ package, name, owner, version, description, categories, deprecated, downloads, rating, url }] }`: a case-insensitive match on the name, then the owner, then the description (exact name, name prefix, name contains, owner, description; within each, not deprecated first, then by downloads); NSFW packages are left out. `limit` 1–50, default 20; `q` 1–100 characters (400 `validation_failed` otherwise). Core downloads the community's package list (`/c/{community}/api/v1/package/`, gzipped) when a search comes and its cache is older than 24 hours, one download at a time; a search with an older cache answers from it meanwhile. The list is kept as a small index in memory and in `cache\thunderstore\<community>.json` under the data root. A failed download keeps the old cache and is not tried again for 5 minutes; with no cache at all, 502 `upstream_unavailable` says to add the mod by its exact name. 409 `invalid_state` when the adapter has no Thunderstore community |
| GET | `/instances/{id}/mods/log?tail=` | `instance.details.read` | The last lines of the loader's log (BepInEx's `BepInEx/LogOutput.log`) in the set that runs or ran last, else the active set → `{ setId, lines }`; `tail` 1–2000, default 200; `setId` null and no lines without a set; no lines before the game wrote the log (phase 2) |
| GET | `/instances/{id}/mods/configs` | `instance.details.read` | The files of the active set's `BepInEx/config`, subfolders included → `{ setId, files: [{ path, size, modified }] }` (`setId` null and no files without an active set) |
| GET | `/instances/{id}/mods/configs/content?path=` | `instance.details.read` | One of those files as text (UTF-8, a byte order mark left out) → `{ setId, path, size, modified, sha256, editable, text }`: `sha256` of the bytes on disk, the `baseSha256` of an edit; `editable` for `.cfg`, `.json`, `.yml`, `.yaml`, `.txt`, `.ini`. 400 `validation_failed` for a path that leaves the folder or is absolute; 404 `not_found` for a missing file or no active set; 409 `invalid_state` for a file over 1 MB |
| PUT | `/instances/{id}/mods/configs/content?path=` | `instance.mods.manage` | `{ content, baseSha256 }` → 200 `{ path, sha256, size, revisionId, running }` ([ADR-0021](../decisions/0021-mod-configs-edited-with-revisions.md), phase 3): writes an existing editable file of the active set's config folder at once, as `gamewarden-run`, through a temporary file in the same folder that replaces it; the text keeps the file's byte order mark and CRLF line ends. If the file on disk differs from its newest revision (or has none), its bytes are stored first as a `server` revision; the new text is stored as `edit`, the newest 50 per file are kept, and an activity line names the file. `running`: the server runs the active set with *Mods on*, so mods that watch their file apply it now and the others at the next restart. 400 `validation_failed` for a path that leaves the folder, an extension that is not editable, content over 1 MB, `.json` that does not parse (comments and trailing commas are allowed; the parser's message), or a missing `content` or `baseSha256`; 404 `not_found` for a missing file or no active set; 409 `config_changed` when the file's SHA-256 is no longer `baseSha256` (nothing is written), `mods_busy` while a mods job runs (an edit holds the same gate), `invalid_state` for a file over 1 MB |
| GET | `/instances/{id}/mods/configs/revisions?path=` | `instance.details.read` | `{ path, revisions: [{ id, kind, sha256, size, createdAt }] }`, newest first; `kind` is `server`, `edit` or `restore`; none before the first save. Revisions belong to the path, not to a set |
| GET | `/instances/{id}/mods/configs/revisions/{revisionId}` | `instance.details.read` | `{ id, path, kind, sha256, size, createdAt, text }`; 404 `not_found` for a revision of another instance or one pruned |
| POST | `/instances/{id}/mods/configs/revisions/{revisionId}/restore` | `instance.mods.manage` | `{ baseSha256 }` → as `PUT …/configs/content`: the revision's bytes are written back as they were and stored as a `restore` revision, with the same rules and answers |
| PUT | `/instances/{id}/mods/enabled` | `instance.mods.manage` | `{ enabled }`: *Mods on* or *off*, applied at the next start → 200 with the state as `GET` returns it; 409 `invalid_state` when the adapter has no `mods`. With *Mods on* and no ready active set, `start` and `restart` answer 409 `invalid_state` ([13 §4.3](13-mods.md#43-folders-and-launch); phase 1b) |
| POST | `/instances/{id}/mods/rollback` | `instance.mods.manage` | Copies the active set's config files over the previous set's (files only in the previous set stay), then swaps the active and previous sets; nothing is rebuilt (phase 3) → 200 with the new state as `GET` returns it; 409 `invalid_state` without a previous set, `mods_busy` while a mods job runs |
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
| 409 | `mods_busy` | A mods job runs for the instance: another mods change, a rollback, a purge or a config edit waits for it ([13 §4.5](13-mods.md#45-sync-job-modssync)) |
| 409 | `config_changed` | A mod config file changed on disk since the edit loaded it; nothing was written ([13 §4.7](13-mods.md#47-configs)) |
| 409 | `port_conflict` | Port used by another instance or process |
| 412 | `prerequisite_missing` | e.g. VC++ runtime missing (`prerequisites` included) |
| 412 | `resources_insufficient` | Below the adapter's `resources.min` (`required` and `available` included); the Owner may retry with `force=true` |
| 502 | `upstream_unavailable` | A service Core asks did not answer as it should after its retries: GitHub for an update, Thunderstore for mods (a 429 that lasts, a 5xx, a timeout) |
| 400 / 401 | `invalid_credentials` | Wrong user name or password (401 at sign-in, 400 for the current password when changing it) |
| 403 | `csrf_failed` | A change made with the session cookie lacks the `X-GW-CSRF` token |
| 403 | `setup_loopback_only` | Creating the Owner from anywhere but the machine itself |
| 403 | `invalid_setup_token` | Wrong or used setup token |
| 409 | `already_set_up` | Setup after the Owner exists |
| 409 | `would_lock_out` | A settings change that would shut out the browser making it |
| 429 | `rate_limited` | Login throttling |
