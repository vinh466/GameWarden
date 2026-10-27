# 09 · Open questions

Close each question by recording the answer here; if it changes architecture, also write an ADR.

| ID | Question | Options | Recommendation | Status |
|---|---|---|---|---|
| OQ-01 | Panel framework | **A.** Blazor WebAssembly + component library (e.g. MudBlazor): one language, DTOs shared with Core. **B.** React + TypeScript + Vite: biggest ecosystem, smaller bundles. | B, with API types generated from OpenAPI | **Closed 2026-10-27** → [ADR-0010](../decisions/0010-panel-react-typescript.md) |
| OQ-02 | Identity of Runner + game processes | **A.** Same as Core (LocalSystem): simplest. **B.** Dedicated non-admin user `gamewarden-run`: Core starts Runners with that user's token; password generated at install, stored with DPAPI. | B | **Closed 2026-10-27** → [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md): B, started with `LogonUser` + `CreateProcessAsUser`; game behaviour checked in S3 |
| OQ-03 | Default panel port | 8080 · 8765 · other | A less common port (e.g. 8765) to avoid clashing with other tools and game ports (2456–2457, 7777, 8211) | Open |
| OQ-04 | Discord login (OAuth) in addition to local accounts | Later / never | Later (after M3), only if friends ask; needs checking whether a VPN-only redirect URL is accepted **[Unverified]** | Open |
| OQ-05 | Can Admins invite users? | Owner only · Admins can invite Players/Operators for their own scope | Owner only in v1 | Open |
| OQ-06 | Installer technology | Inno Setup · WiX | Inno Setup (simpler script, enough for one service + files) | Open |
| OQ-07 | Resource guardrails | None · warn when starting would exceed free RAM · hard block | Warn (show estimated RAM from adapter vs free RAM) | Open |
| OQ-08 | Off-VM copy of backups | None in v1 · copy to a network share / cloud folder | Plan for v1.x; until then backups die with the VM disk. See OQ-23 for moving it earlier. | Open |
| OQ-09 | Remote repo hosting and visibility | Local only · private GitHub · public + license | Private remote first; decide license if it goes public | Open |
| OQ-10 | Player "request restart" | No · button that notifies Operators/Discord · vote | Notify-only button, after M3 | Open |
| OQ-11 | Show game password to Players via Share | On by default · off by default | On (friends-only); Owner can turn off per instance | Proposed in spec |
| OQ-12 | Games that need a Steam login in SteamCMD | Not supported · supported with stored credentials (Steam Guard issues) | Not supported in v1; adapter declares `requiresLogin` and falls back to a guide | Proposed in spec |
| OQ-13 | Relationship with `gamehost-infra` (Pelican/Linux plan in the same workspace) | Keep both · archive one | Decide which plan is active to avoid two diverging designs | Open |

## From the spec review (2026-10-27)

| ID | Question | Options | Recommendation | Status |
|---|---|---|---|---|
| OQ-14 | Secrets that a game keeps in its own config file (e.g. Palworld `AdminPassword` in an INI) are plaintext in `server\` / `config\` and in backups | Accept and document · strip them from backups | Accept and document: downloading a backup already needs `instance.backup.restore` (Admin) | Open |
| OQ-15 | `Busy` mixes process state and job lock: a hot backup runs while `Running`, and an update starts from `Running`, but 03 §3 only allows `Stopped / Faulted / NotInstalled → Busy` | Keep `Busy` · split into `actualState` + `activeJobId` | Split; the panel combines both into one badge | Open |
| OQ-16 | Does `Faulted` survive a Core or VM restart? | Persist · reset on restart | Persist, and do not auto-start a `Faulted` instance | Open |
| OQ-17 | Source of truth: SQLite or `instance.json`? (02 §7 says either can rebuild the other) | DB · files | DB; `instance.json` is an export used only for manual rebuilds | Open |
| OQ-18 | Where built-in adapters load from (08 §1 copies them into the data root) | Copy to data root · load from Program Files | Load built-ins from `Program Files` (read-only); Owner adapters in the data root override by `id`; nothing to reconcile on upgrade | Open |
| OQ-19 | Readiness source and resource metrics | stdout only · stdout or a log file | `readiness.source`: `stdout` or `file:<path>`, because some servers log to files or buffer stdout; measure CPU/RAM per Job Object, not per PID (launcher exes) | Open |
| OQ-20 | Restore is "clear `data\` → extract" (08 §4); a failed extract leaves no world | As is · extract to temp, then swap | Extract to a temporary folder, then swap | Open |
| OQ-21 | VM shutdown and Windows Update reboots: a service gets little time at shutdown | As is · PRESHUTDOWN + update policy | Register `SERVICE_CONTROL_PRESHUTDOWN` with an explicit timeout; set active hours / deferral on the VM and document it in 08 | Open |
| OQ-22 | Small inconsistencies between docs | – | (a) `update-check` needs `instance.update`, since it takes the global SteamCMD lock; (b) decide what `instance.view` returns to Players (the API exposes jobs and job logs, 07 §9 shows them only Overview); (c) `audit.read` for Admin (06 §5.2) vs Owner-only `/audit` (05); (d) Host filtering or a one-time setup token for `/setup/owner`; (e) check `Origin` on the SignalR hub; (f) time zone of schedules (VM local time) | Open |
| OQ-23 | v1 scope | As specified · cut | Global roles only (keep the `scope` column for later); Owner creates accounts, invites later; activity feed instead of an Audit page; renderers `arg` + `ini` + `hook` first; move OQ-08 earlier as a simple "copy to folder"; Operator role already in M2 | Open |

## Spikes needed before M1 (answers feed back into the spec)

| Spike | Question to answer | Status |
|---|---|---|
| S1 Ctrl+C delivery | Does the Runner console trick deliver Ctrl+C to Valheim and save the world? | Part 1 done 2026-10-27 on the dev machine ([spikes/s1-ctrlc](../spikes/s1-ctrlc/)): works when the Runner resets the inherited CTRL+C flag; fails with an inherited ignore flag or `CREATE_NEW_PROCESS_GROUP`. Part 2 (VM, service, real Valheim) open. |
| S2 Session 0 | Do Valheim, Palworld, Terraria servers run correctly when started from a service (no desktop)? | Open |
| S3 Run-as user | Can Core (LocalSystem) start a Runner as `gamewarden-run` with the ADR-0008 chain (`LogonUser` → `LoadUserProfile` → `CreateEnvironmentBlock` → `CreateProcessAsUser`, `lpDesktop = ""`), and does the game work under it? Which paths does it write outside `instances\<id>` (diff `C:\Users\gamewarden-run` before/after)? | Open |
| S4 SteamCMD | Output format for progress parsing; build-id check for "update available"; behavior when two SteamCMD processes run; SteamCMD running as `gamewarden-run`. | Open |
| S5 Prerequisites | Exact silent-install commands and sources for VC++ and DirectX runtime; does Valheim start after installing them on the template VM? | Open |
| S6 Discord | Can a webhook keep one live status message up to date? | Answered 2026-10-27 from Discord's webhook docs: Execute Webhook with `?wait=true` returns the created message; Edit Webhook Message (`PATCH /webhooks/{id}/{token}/messages/{message.id}`) edits it. No webhook endpoint pins messages: pin it once by hand, or use a bot later. |
