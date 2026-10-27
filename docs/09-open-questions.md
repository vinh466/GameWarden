# 09 · Open questions

Close each question by recording the answer here; if it changes architecture, also write an ADR.

## Open

| ID | Question | Options | Recommendation | Status |
|---|---|---|---|---|
| OQ-04 | Discord login (OAuth) in addition to local accounts | Later / never | Later (after M6), only if friends ask; needs checking whether a VPN-only redirect URL is accepted **[Unverified]** | Open |
| OQ-05 | Can Admins invite users? | Owner only · Admins can invite Players/Operators for their own scope | Owner only (M6) | Open |
| OQ-10 | Player "request restart" | No · button that notifies Operators/Discord · vote | Notify-only button, after M6 | Open |
| OQ-11 | Show game password to Players via Share | On by default · off by default | On (friends-only); Owner can turn off per instance | Proposed in spec |
| OQ-12 | Games that need a Steam login in SteamCMD | Not supported · supported with stored credentials (Steam Guard issues) | Not supported in v1; adapter declares `requiresLogin` and falls back to a guide | Proposed in spec |

## Closed

| ID | Question | Answer | Closed |
|---|---|---|---|
| OQ-01 | Panel framework | React + TypeScript + Vite, API types generated from OpenAPI → [ADR-0010](../decisions/0010-panel-react-typescript.md) | 2026-10-27 |
| OQ-02 | Identity of Runner + game processes | Non-admin user `gamewarden-run`, started with `LogonUser` + `CreateProcessAsUser`; game behaviour checked in S3 → [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md) | 2026-10-27 |
| OQ-03 | Default panel port | **8765** (clear of game ports 2456–2457, 7777, 8211) | 2026-10-27 |
| OQ-06 | Installer technology | Own Setup engine in C#, also used for updates; no Inno Setup / WiX → [ADR-0013](../decisions/0013-own-setup-and-update-engine.md) | 2026-10-27 |
| OQ-07 | Resource guardrails | Adapters declare `resources.min` and `resources.recommended`; below min the start is blocked (Owner may force), below recommended it warns ([04 §3](04-adapter-spec.md#3-adapterjson)). Several instances may run at once; the VM can be scaled up. → [ADR-0011](../decisions/0011-mvp-scope-and-direction.md) | 2026-10-27 |
| OQ-08 | Off-VM copy of backups | Out of scope. All backups are collected in one configurable folder (`backupRoot`); the operator copies it wherever they want ([08 §4](08-operations.md#4-backups)) → [ADR-0011](../decisions/0011-mvp-scope-and-direction.md) | 2026-10-27 |
| OQ-09 | Remote repo hosting and visibility | Public GitHub repository, CI on GitHub Actions; the README states problem, direction and scope → [ADR-0011](../decisions/0011-mvp-scope-and-direction.md) | 2026-10-27 |
| OQ-13 | Relationship with `gamehost-infra` (Pelican/Linux plan) | GameWarden is the active plan; Pelican/Linux is a technical reference only → [ADR-0011](../decisions/0011-mvp-scope-and-direction.md) | 2026-10-27 |
| OQ-14 | Secrets that a game keeps in its own config file (e.g. Palworld `AdminPassword`) are plaintext there and in backups | Accepted and documented ([06 §6](06-security-and-access.md#6-secrets)); downloading a backup needs `instance.backup.restore` | 2026-10-27 |
| OQ-15 | `Busy` mixed process state and job lock | No `Busy` state; `activeJobId` marks the job lock and the panel shows a *Busy* badge ([03 §2](03-domain-model.md#2-instance-states)) | 2026-10-27 |
| OQ-16 | Does `Faulted` survive a Core / VM restart? | Yes; a `Faulted` instance is never started automatically ([03 §4](03-domain-model.md#4-crash-and-restart-policy)) | 2026-10-27 |
| OQ-17 | Source of truth: SQLite or `instance.json`? | SQLite; `instance.json` is an export for manual rebuilds ([02 §7](02-architecture.md#7-persistence)) | 2026-10-27 |
| OQ-18 | Where built-in adapters load from | Read-only from `Program Files`; the Owner's adapters in the data root override by `id` ([04 §2](04-adapter-spec.md#2-folder-layout)) | 2026-10-27 |
| OQ-19 | Readiness source and resource metrics | `readiness.source`: `stdout` or `file:<path>`; CPU/RAM measured per Job Object, not per PID ([04 §3](04-adapter-spec.md#3-adapterjson)) | 2026-10-27 |
| OQ-20 | Restore was "clear `data\` → extract" | Extract to a temporary folder, then swap ([08 §4](08-operations.md#4-backups)) | 2026-10-27 |
| OQ-21 | VM shutdown and Windows Update reboots | `SERVICE_CONTROL_PRESHUTDOWN` with an explicit timeout through an own service host ([ADR-0014](../decisions/0014-service-host-with-preshutdown.md)); active hours on the VM ([08 §9](08-operations.md#9-shutdown-and-windows-update)) | 2026-10-27 |
| OQ-22 | Small inconsistencies between docs | (a) `update-check` needs `instance.update`; (b) new permission `instance.details.read` for jobs, job logs, backups, schedules and mods, so Players see only Overview; (c) `audit.read` is Owner-only; (d) host filtering plus a one-time setup token for `/setup/owner`; (e) the SignalR hub checks `Origin`; (f) schedules run in the VM's local time zone ([05](05-api.md), [06 §5](06-security-and-access.md#5-authorization)) | 2026-10-27 |
| OQ-23 | v1 scope | MVP = one Owner; accounts, roles and invites after the MVP (M6); renderers `arg` + `ini` + `hook` first → [ADR-0011](../decisions/0011-mvp-scope-and-direction.md), [ROADMAP](../ROADMAP.md) | 2026-10-27 |
| OQ-24 | License of the public repository | **MIT** ([LICENSE](../LICENSE)) | 2026-10-27 |
| OQ-25 | Concurrency: user actions, schedules, Runner events, jobs and the reconciler all change one instance | One serialized queue per instance (`Channel<T>`); nothing else changes an instance's state ([02 §5](02-architecture.md#5-desired-state-and-reconciliation)) | 2026-10-27 |
| OQ-26 | Old Runners after an update; a mod could fake a Runner pipe | Version handshake, Core supports protocol N and N-1; Core checks the pipe server's PID and start time ([02 §4.2](02-architecture.md#42-lifecycle)) | 2026-10-27 |
| OQ-27 | Core's own state is not in any backup | Nightly `VACUUM INTO` of the DB plus `gamewarden.json` into `<backupRoot>\_core\` (keep 14) ([08 §4](08-operations.md#4-backups)) | 2026-10-27 |
| OQ-28 | Binding to a VPN address fails when the VPN adapter comes up after the service | Listen on `0.0.0.0:8765`; accept only connections whose local address is loopback or inside a VPN range ([06 §2](06-security-and-access.md#2-network-exposure)) | 2026-10-27 |
| OQ-29 | A damaged backup is found only when needed | CRC of every entry checked after writing; SHA-256 stored and checked again on restore ([08 §4](08-operations.md#4-backups)) | 2026-10-27 |
| OQ-30 | A full disk breaks SQLite, logs, backups and updates | Below 5 GB or 10 % free: prune old scheduled backups, then block backups, installs and updates and alert on Discord ([08 §4](08-operations.md#4-backups)) | 2026-10-27 |
| OQ-31 | Two Cores on one machine | Global named mutex; the second Core exits with a clear message ([02 §2](02-architecture.md#2-components)) | 2026-10-27 |
| OQ-32 | Hard power-off loses up to Valheim's save interval (default 1800 s) | `-saveinterval 600` in the Valheim adapter **[value to confirm in S2]** ([04 §9](04-adapter-spec.md#9-example-valheim-l1--l3)) | 2026-10-27 |
| OQ-33 | Transient SteamCMD failures | Retry with back-off, 3 tries; spike S4 lists the error codes worth retrying ([03 §5](03-domain-model.md#5-jobs)) | 2026-10-27 |

## Spikes needed before M1 (answers feed back into the spec)

Spikes run by hand on the dev machine or the template VM, like the manual tests in [10 §8](10-build-test-release.md#8-manual-tests-on-the-vm).

| Spike | Question to answer | Status |
|---|---|---|
| S1 Ctrl+C delivery | Does the Runner console trick deliver Ctrl+C to Valheim and save the world? | Part 1 done 2026-10-27 on the dev machine ([spikes/s1-ctrlc](../spikes/s1-ctrlc/)): works when the Runner resets the inherited CTRL+C flag; fails with an inherited ignore flag or `CREATE_NEW_PROCESS_GROUP`. Part 2 (VM, service, real Valheim; check `SteamAppId`) open. |
| S2 Session 0 | Do Valheim, Palworld, Terraria servers run correctly when started from a service (no desktop)? Measure CPU / RAM / disk for `resources.min` and `recommended`, and how long a Valheim save takes. | Open |
| S3 Run-as user | Can Core (LocalSystem) start a Runner as `gamewarden-run` with the ADR-0008 chain (`LogonUser` → `LoadUserProfile` → `CreateEnvironmentBlock` → `CreateProcessAsUser`, `lpDesktop = ""`), and does the game work under it? Which paths does it write outside `instances\<id>` (diff `C:\Users\gamewarden-run` before/after)? | Open |
| S4 SteamCMD | Output format for progress parsing; build-id check for "update available"; behavior when two SteamCMD processes run; SteamCMD running as `gamewarden-run`; error codes worth retrying. | Open |
| S5 Prerequisites | Exact silent-install commands and sources for VC++ and DirectX runtime; does Valheim start after installing them on the template VM? | Open |
| S6 Discord | Can a webhook keep one live status message up to date? | Answered 2026-10-27 from Discord's webhook docs: Execute Webhook with `?wait=true` returns the created message; Edit Webhook Message (`PATCH /webhooks/{id}/{token}/messages/{message.id}`) edits it. No webhook endpoint pins messages: pin it once by hand, or use a bot later. |
| S7 Radmin VPN | Friends mostly use Radmin VPN. Which address range does it use (`26.0.0.0/8`?), does it encrypt traffic, and which Windows network profile does its adapter get (affects firewall rules)? | Open |
| S8 Pre-shutdown | On the template VM: does the ADR-0014 service host get `PRESHUTDOWN` with a 120 s timeout, how long does Valheim need to save, and what is the hypervisor's shutdown timeout? | Open |
