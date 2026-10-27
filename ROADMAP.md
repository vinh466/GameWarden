# Roadmap

Each milestone ends with something usable and has explicit exit criteria. Estimates are deliberately omitted until M0 is done.

## M0 · Spikes and prerequisites

- [ ] Run spikes S1–S6 from [09-open-questions](docs/09-open-questions.md#spikes-needed-before-m1-answers-feed-back-into-the-spec)
- [ ] Close OQ-01 (panel framework), OQ-02 (run-as user), OQ-03 (port)
- [ ] Set up the Windows dev machine ([02 §11](docs/02-architecture.md#11-development-environment))
- [ ] Update the spec with spike results (remove `[Unverified]` where confirmed)

**Exit:** Valheim started and stopped (with save) from a service through a prototype Runner on the template VM.

## M1 · Core skeleton + Valheim L1 (CLI only)

- [ ] Solution layout (`Core`, `Runner`, `Cli`, `Contracts`, tests)
- [ ] Windows Service host, SQLite + migrations, data root layout
- [ ] Adapter loader + JSON Schema validation; `fakegame` dev adapter; `valheim` adapter at L1
- [ ] SteamCMD bootstrap + install/update jobs; job queue with locks
- [ ] Supervisor: Runner start/attach/stop, readiness, restart policy, reconciliation
- [ ] Port allocation + firewall rules
- [ ] CLI: `status`, `start`, `stop`, `restart`, `logs`
- [ ] Prerequisite checks (detect only)

**Exit:** after a VM reboot, `valheim-main` is back up without login; killing the game triggers an automatic restart.

## M2 · Web panel MVP (Owner only)

- [ ] First-run setup wizard (Owner account, prerequisites with install, network)
- [ ] Login, session, CSRF
- [ ] Dashboard, server detail (Overview, Console), new server wizard
- [ ] Realtime hub: state, job progress, console
- [ ] Visibility: favicon/title status, header heartbeat, connection-lost banner, "Core restarted" notice

**Exit:** success criterion 1 in [01 §8](docs/01-vision-and-scope.md#8-success-criteria-for-v1) passes.

## M3 · Friends: accounts, roles, audit

- [ ] Permissions + roles + scoped bindings
- [ ] Invites, user management
- [ ] Audit log + activity feed with user names
- [ ] Role-aware UI
- [ ] Share button (connect info)

**Exit:** success criterion 4 passes.

## M4 · Backups, schedules, safe updates

- [ ] Backup / restore / prune; pre-operation backups
- [ ] Scheduler (backup, restart, update-check)
- [ ] Update flow as one composite job
- [ ] Archive / purge instance

**Exit:** success criterion 5 passes.

## M5 · Settings forms + more games

- [ ] Settings schema → forms, renderers (`arg`, `ini`, `json`, `keyvalue`, `env`, `hook`)
- [ ] `valheim` L2, `palworld` L2 (renderConfig hook), `terraria` L1–L2
- [ ] L0 guide rendering

## M6 · Notifications and monitoring

- [ ] Discord webhook events + pinned live status message
- [ ] `/healthz`, Windows Event Log entries
- [ ] Player probes (A2S / log / REST)

**Exit:** success criterion 3 passes (crash → restart → Discord message).

## M7 · Mods (L3)

- [ ] Thunderstore provider (Valheim / BepInEx)
- [ ] Steam Workshop provider
- [ ] Mods tab, mod list in Share text

## M8 · Installer and upgrades

- [ ] Installer (OQ-06), service registration, shortcuts
- [ ] Upgrade with running games kept alive; versioned Runner
- [ ] Uninstall (keep / remove data)

## Backlog

- Off-VM backup copy (OQ-08)
- Discord login (OQ-04), Player "request restart" (OQ-10)
- Resource guardrails (OQ-07)
- HTTPS (would supersede ADR-0005), PWA / push
- Public status page
