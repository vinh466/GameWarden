# 03 · Domain model

## 1. Entities

| Entity | Key fields | Notes |
|---|---|---|
| **Adapter** | `id` (e.g. `valheim`), `version`, `schemaVersion`, `level` (L0–L3) | Loaded from disk; read-only to everyone but the Owner. |
| **Instance** | `id` (slug), `adapterId`, `adapterVersion`, `displayName`, `desiredState`, `actualState`, `activeJobId?`, `ports[]`, `settings{}`, `autoStart`, `restartPolicy`, `createdBy`, `createdAt` | `id` is immutable, lowercase kebab-case, unique (e.g. `valheim-main`). `actualState` is the process state (§2); `activeJobId` is set while an exclusive job holds the instance lock (§5). |
| **Job** | `id`, `type`, `instanceId?`, `state`, `progress`, `startedBy`, `log` | See §5. |
| **Backup** | `id`, `instanceId`, `createdAt`, `reason`, `sizeBytes`, `path`, `pinned` | `reason`: `manual` · `scheduled` · `pre-update` · `pre-restore` · `pre-delete`. |
| **Schedule** | `id`, `instanceId`, `action`, `cron`, `enabled` | Actions: `backup`, `restart`, `update-check`. Cron runs in the VM's local time zone (OQ-22). |
| **User** | `id`, `username`, `passwordHash`, `isOwner`, `disabled`, `lastLoginAt` | |
| **RoleBinding** | `userId`, `role`, `scope` (`global` or `instance:<id>`) | A user can have several bindings. |
| **Invite** | `token`, `role`, `scope`, `expiresAt`, `usedBy?` | Single use. |
| **AuditEvent** | `at`, `userId`, `action`, `target`, `result`, `details` | Append-only. |
| **Secret** | `name`, `scope`, `ciphertext` | DPAPI-encrypted; never returned by the API, only "set / not set". |
| **InstanceMods** | `instanceId`, `enabled`, `activeSetId?`, `previousSetId?`, `runningSetId?`, `shareCode?`, `shareCodeCreatedAt?` | Table `instance_mods`, one row per instance with mods ([13 §4.2](13-mods.md#42-data)). The next start uses the active set; rollback swaps active and previous. `enabled` is *Mods on*, set with `PUT /mods/enabled` and applied at the next start: on, a start without a ready active set is refused; off, the loader's off arguments keep the game vanilla. `runningSetId` is the set the last start with *Mods on* loaded, recorded by the launch path (crash restarts and starts after a job too); it is never pruned ([13 §4.3](13-mods.md#43-folders-and-launch)). Since 2026-10-31 a new set gets the active set's configs when it is built ([13 §4.7](13-mods.md#47-configs), [ADR-0021](../decisions/0021-mod-configs-edited-with-revisions.md)). The share code is used from phase 1c. |
| **ModSet** | `id`, `instanceId`, `createdAt`, `origin` (`code` · `edit` · `update` · `rollback`), `state` (`building` · `ready` · `failed`), `jobId` | Table `mod_sets`. Built once into `{paths.mods}\sets\<id>\` and never changed once `ready` (its config files excepted, [13 §4.7](13-mods.md#47-configs)); a set that is neither active, previous nor running is deleted at the next activation. Its config folder is assembled in the build: the mods' defaults, then the active set's files, then the code's. (`startedAt`, which marked a set's first start for the old carry-over, was dropped on 2026-10-31.) |
| **ModEntry** | `setId`, `package` (`Namespace-Name`), `version`, `enabled`, `origin` (`chosen` · `dependency`) | Table `mod_entries`. A disabled entry stays in the set and is not installed. |
| **ModConfigRevision** | `id`, `instanceId`, `path`, `kind` (`server` · `edit` · `restore`), `sha256`, `size`, `content`, `createdAt` | Table `mod_config_revisions` ([ADR-0021](../decisions/0021-mod-configs-edited-with-revisions.md)): a config file's bytes, per instance and path relative to `BepInEx/config`, so revisions survive set changes; gone with the instance. Each save stores `edit` or `restore`; before it, a file on disk that differs from the newest revision is stored as `server`. The newest 50 per file are kept. |
| **PackageVersion** | `package`, `version`, `sha256`, `size`, `firstSeenAt` | Table `package_versions`, shared by every instance: the hash recorded at a version's first download, which every later download and build must match (trust on first use). |

## 2. Instance states

| State | Meaning |
|---|---|
| `NotInstalled` | Created, game files not downloaded yet. |
| `Stopped` | Installed, not running. |
| `Starting` | Runner launched, waiting for readiness probe. |
| `Running` | Readiness probe passed. |
| `Stopping` | Graceful stop in progress. |
| `Crashed` | Process exited without a stop request; restart pending (back-off). |
| `Faulted` | Restart limit reached, or start failed repeatedly. Needs a human. |

There is no `Busy` state (OQ-15). While an exclusive job holds the instance lock (install, update, restore, mod sync, delete), `activeJobId` is set, and the job drives the process state through the normal transitions: an update moves a running server `Running → Stopping → Stopped → Starting → Running`. The panel shows a yellow *Busy* badge whenever `activeJobId` is set.

## 3. State machine

Diagram: [05-instance-state-machine](diagrams/05-instance-state-machine.excalidraw).

```
             install job
NotInstalled ───────────► Stopped ◄──────────────────────────────┐
                            │ start                              │
                            ▼                                    │ exited after stop
                         Starting ── ready ──► Running ── stop ──► Stopping
                            │                     │
                  start failed / timeout          │ exited unexpectedly
                            ▼                     ▼
                         Crashed ◄────────────────┘
                            │ back-off elapsed && attempts < limit ─► Starting
                            │ attempts >= limit
                            ▼
                         Faulted ── user start ─► Starting  (counter reset)

The job lock (activeJobId) is orthogonal to these states.
```

| Event | From | To | Notes |
|---|---|---|---|
| `start` | Stopped, Faulted | Starting | Sets desired = running. Rejected while a job holds the lock (409 `instance_busy`) and below the adapter's minimum resources (412 `resources_insufficient`; the Owner may force it). |
| `ready` | Starting | Running | From adapter readiness probe. |
| `startTimeout` | Starting | Crashed | Adapter `startTimeoutSec`. |
| `stop` | Starting, Running, Crashed | Stopping / Stopped | Sets desired = stopped; cancels pending restart. |
| `exited` | Running, Starting | Crashed | Unexpected exit. |
| `exited` | Stopping | Stopped | Expected exit. |
| `restart` | Running | Stopping → Starting | Desired stays running. |
| `jobAcquire` / `jobRelease` | any | unchanged | Sets / clears `activeJobId`. Jobs that need the server stopped stop it through the normal `stop` path first (e.g. update). |

## 4. Crash and restart policy

Defaults (overridable per instance):

| Setting | Default |
|---|---|
| Back-off delays | 10 s, 30 s, 60 s, 120 s, 300 s |
| Max restarts in window | 5 within 30 min → `Faulted` |
| Reset | Counter resets after 30 min in `Running`, or on a manual start |
| Persistence | `Faulted` survives Core and VM restarts; a `Faulted` instance is never started automatically (OQ-16) |

Every crash emits an `instance.crashed` event (Discord + activity feed) with the last 50 console lines attached to the activity entry.

## 5. Jobs

| Field | Meaning |
|---|---|
| `type` | `install`, `update`, `backup`, `restore`, `mods.sync`, `delete`, `prereq.install`, `steamcmd.bootstrap`, `update-check` |
| `state` | `Queued` → `Running` → `Succeeded` / `Failed` / `Cancelled` |
| `progress` | 0–100 or `null` (indeterminate), plus a short `stage` text (e.g. "Downloading 45%") |
| `log` | Line-oriented, stored on disk, streamed live |
| `locks` | Instance lock (exclusive) and/or global SteamCMD lock; `mods.sync` takes neither (below) |

Rules:

- One exclusive job per instance at a time; others wait in the queue (visible in the panel).
- At most one job holds the SteamCMD lock at a time.
- SteamCMD stages retry transient failures with back-off, 3 tries; spike S4 lists the error codes worth retrying (OQ-33).
- Job steps change an instance's state only through that instance's queue (OQ-25, [02 §5](02-architecture.md#5-desired-state-and-reconciliation)).
- Composite operations are one job with stages, e.g. **update** = `announce` → `stop` → `pre-backup` → `steamcmd update` → `start` (start only if desired = running).
- **`mods.sync`** = `resolve` → `download` → `verify` → `build` → `activate` ([13 §4.5](13-mods.md#45-sync-job-modssync)). The build extracts the packages (their default config files land in `BepInEx\config\`), then copies the active set's `BepInEx\config\` over them, then a code's config files (later files win). It never stops or starts the server and holds no instance lock: the new set takes effect at the next start. One mods job runs per instance (a second one, a rollback, a purge or a config edit gets 409 `mods_busy`). Thunderstore requests retry timeouts, 429 (honouring `Retry-After` up to 60 s) and 5xx, 3 tries; a 404 fails the job with the package's name. Downloads land in `tmp\mods\<jobId>\` under the data root, which only Core writes, and are copied into `{paths.mods}\cache\` as `gamewarden-run`. A package not listed in the adapter's Thunderstore community is a warning in the job log; one that unpacks to more than `ModPackageMaxBytes` (2 GiB, counted as written) fails the build. A failed build marks its set `failed` and leaves the active set as it was. `{paths.mods}\mods.json` is rewritten at each activation and rollback: `{ note, instanceId, setId, createdAt, loader, mods: [{ package, version, enabled, origin }] }`, a copy for people and manual backups, never read by Core. A Core restart fails a running `mods.sync` and its `building` set.
- Cancelling a job runs its cleanup stage; cancelling during SteamCMD leaves files as SteamCMD left them and marks the instance "needs verify".

## 6. Events (event bus)

| Event | Raised when | Typical subscribers |
|---|---|---|
| `instance.stateChanged` | Any state transition, or `activeJobId` set / cleared | Realtime, Discord status message |
| `instance.crashed` / `instance.faulted` | Unexpected exit / restart limit | Discord, activity |
| `instance.output` | Console line | Realtime (console subscribers only) |
| `instance.playersChanged` | Player probe result changed | Realtime, Discord status message |
| `job.created` / `job.progress` / `job.finished` | Job lifecycle | Realtime, activity, Discord (failures) |
| `backup.created` / `backup.failed` | Backups | Activity, Discord (failures) |
| `update.available` | Update check found a new build | Panel badge, Discord |
| `core.started` | Core start (with "recovered after failure" flag) | Activity, Discord |
| `user.action` | Any audited action | Audit |
