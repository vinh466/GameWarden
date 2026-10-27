# Roadmap

Each milestone ends with something usable and has explicit exit criteria. Estimates are deliberately omitted until M0 is done.

**MVP = M0–M4:** one Owner runs Valheim unattended on the Windows VM, with backups collected in one folder and Discord notifications ([ADR-0011](decisions/0011-mvp-scope-and-direction.md)). Milestones after M4 build on it without changing the MVP's contracts. Success criteria: [01 §8](docs/01-vision-and-scope.md#8-success-criteria).

## MVP

### M0 · Spikes and prerequisites

- [ ] Run spikes S1–S7 from [09-open-questions](docs/09-open-questions.md#spikes-needed-before-m1-answers-feed-back-into-the-spec) (S1 part 1 and S6 answered 2026-10-27)
- [x] Close OQ-01 (panel framework → [ADR-0010](decisions/0010-panel-react-typescript.md)) and OQ-02 (run-as user → [ADR-0008](decisions/0008-run-as-user-and-launch-chain.md))
- [x] Close OQ-07, OQ-08, OQ-09, OQ-13 to OQ-23 (2026-10-27, [ADR-0011](decisions/0011-mvp-scope-and-direction.md) and [09](docs/09-open-questions.md))
- [x] Close OQ-03 (port 8765) and OQ-24 (MIT license)
- [x] Public GitHub repository
- [ ] CI on GitHub Actions for the docs (relative links, diagram files parse)
- [ ] Set up the dev machine: the Owner's personal Windows 11 PC ([02 §11](docs/02-architecture.md#11-development-environment)); the game VM stays clean
- [ ] Update the spec with spike results (remove `[Unverified]` where confirmed)

**Exit:** Valheim started and stopped (with save) from a service through a prototype Runner on the template VM.

### M1 · Core skeleton + Valheim L1 (CLI only)

- [ ] Solution layout (`Core`, `Runner`, `Cli`, `Contracts`, tests)
- [ ] Windows Service host (with pre-shutdown handling), SQLite + migrations, data root layout
- [ ] `gamewarden-run` user, data-root ACL, launch chain ([ADR-0008](decisions/0008-run-as-user-and-launch-chain.md), [ADR-0009](decisions/0009-data-root-acl-and-secrets.md))
- [ ] Adapter loader + JSON Schema validation; `fakegame` dev adapter; `valheim` adapter at L1
- [ ] SteamCMD bootstrap + install/update jobs; job queue with locks
- [ ] Supervisor: Runner start/attach/stop, readiness, restart policy, reconciliation
- [ ] Port allocation + firewall rules; resource checks (min / recommended)
- [ ] CLI: `status`, `start`, `stop`, `restart`, `logs`
- [ ] Prerequisite checks (detect only)

**Exit:** after a VM reboot, `valheim-main` is back up without login; killing the game triggers an automatic restart.

### M2 · Web panel (Owner only)

- [ ] First-run setup wizard (Owner account with the one-time setup token, prerequisites with install, network)
- [ ] Login, session, CSRF, host filtering
- [ ] Dashboard, server detail (Overview, Console), new server wizard with resource check
- [ ] Realtime hub: state, job progress, console
- [ ] Visibility: favicon/title status, header heartbeat, connection-lost banner, "Core restarted" notice

**Exit:** success criterion 1 passes.

### M3 · Backups, schedules, safe updates

- [ ] Backup / restore (extract to a temporary folder, then swap) / prune; pre-operation backups; one configurable backup folder
- [ ] Scheduler (backup, restart, update-check) in the VM's local time
- [ ] Update flow as one composite job
- [ ] Archive / purge instance

**Exit:** success criterion 4 passes.

### M4 · Discord notifications and monitoring

- [ ] Discord webhook events + one live status message edited in place (pinned once by hand)
- [ ] `/healthz`, Windows Event Log entries
- [ ] Player probes (A2S / log / REST)

**Exit:** success criteria 2, 3 and 5 pass. **MVP done.**

## After the MVP

### M5 · Settings forms + more games

- [ ] Settings schema → forms; renderers `arg`, `ini`, `hook` first, then `json`, `keyvalue`, `env`
- [ ] `valheim` L2, `palworld` L2 (renderConfig hook), `terraria` L1–L2
- [ ] L0 guide rendering

### M6 · Friends: accounts, roles, audit

- [ ] Permissions + roles + scoped bindings ([ADR-0006](decisions/0006-rbac-with-separate-owner.md))
- [ ] Invites, user management
- [ ] Audit page + activity feed with user names
- [ ] Role-aware UI
- [ ] Share button (connect info)

**Exit:** success criterion 6 passes.

### M7 · Mods (L3)

- [ ] Thunderstore provider (Valheim / BepInEx)
- [ ] Steam Workshop provider
- [ ] Mods tab, mod list in Share text

### M8 · Installer and upgrades

- [ ] Installer (OQ-06), service registration, shortcuts
- [ ] Upgrade with running games kept alive; versioned Runner
- [ ] Uninstall (keep / remove data)

Until M8, GameWarden is deployed by copying a self-contained publish and running `gamewarden install` ([02 §11](docs/02-architecture.md#11-development-environment)).

## Backlog

- Discord login (OQ-04), Player "request restart" (OQ-10)
- HTTPS (would supersede ADR-0005), PWA / push
- Public status page
- One run-as user per instance ([ADR-0008](decisions/0008-run-as-user-and-launch-chain.md))
