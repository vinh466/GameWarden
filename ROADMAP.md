# Roadmap

Each milestone ends with something usable and has explicit exit criteria. Estimates are deliberately omitted until M0 is done.

**MVP = M0–M4:** one Owner runs Valheim unattended on the Windows VM, with backups collected in one folder and Discord notifications ([ADR-0011](decisions/0011-mvp-scope-and-direction.md)). Milestones after M4 build on it without changing the MVP's contracts. Success criteria: [01 §8](docs/01-vision-and-scope.md#8-success-criteria).

## MVP

### M0 · Spikes and prerequisites

- [ ] Run spikes S1–S8 from [09-open-questions](docs/09-open-questions.md#spikes-needed-before-m1-answers-feed-back-into-the-spec) (S1 part 1 and S6 answered 2026-10-27)
- [x] Close OQ-01 (panel framework → [ADR-0010](decisions/0010-panel-react-typescript.md)) and OQ-02 (run-as user → [ADR-0008](decisions/0008-run-as-user-and-launch-chain.md))
- [x] Close OQ-07, OQ-08, OQ-09, OQ-13 to OQ-23 (2026-10-27, [ADR-0011](decisions/0011-mvp-scope-and-direction.md) and [09](docs/09-open-questions.md))
- [x] Close OQ-03 (port 8765) and OQ-24 (MIT license)
- [x] Public GitHub repository
- [ ] CI skeleton ([ADR-0012](decisions/0012-ci-and-tests-on-github-hosted-runners.md)): `ci.yml` with the docs job now; the .NET and panel jobs as soon as `src/` exists; Dependabot, CodeQL, `SECURITY.md`, branch protection on `main`
- [ ] Set up the dev machine: the Owner's personal Windows 11 PC ([02 §11](docs/02-architecture.md#11-development-environment)); the game VM stays clean
- [ ] Update the spec with spike results (remove `[Unverified]` where confirmed)

**Exit:** Valheim started and stopped (with save) from a service through a prototype Runner on the template VM.

### M1 · Core skeleton + Valheim L1 (CLI only)

- [ ] Solution layout (`Core`, `Runner`, `Cli`, `Contracts`, tests)
- [ ] Windows Service host with pre-shutdown ([ADR-0014](decisions/0014-service-host-with-preshutdown.md)), SQLite + migrations, data root layout
- [ ] `gamewarden-run` user, data-root ACL, launch chain ([ADR-0008](decisions/0008-run-as-user-and-launch-chain.md), [ADR-0009](decisions/0009-data-root-acl-and-secrets.md))
- [ ] Adapter loader + JSON Schema validation; `fakegame` dev adapter; `valheim` adapter at L1
- [ ] SteamCMD bootstrap + install/update jobs; job queue with locks
- [ ] Supervisor: Runner start/attach/stop, readiness, restart policy, reconciliation; one queue per instance (OQ-25); pipe handshake + PID check (OQ-26); one Core per machine (OQ-31)
- [ ] Port allocation + firewall rules; resource checks (min / recommended)
- [ ] CLI: `status`, `start`, `stop`, `restart`, `logs`
- [ ] Prerequisite checks (detect only)
- [ ] `FakeGame`; `Unit`, `Api`, `Windows` tests in CI; dev-build artifact for every push to `main` ([10 §2–3](docs/10-build-test-release.md#2-test-strategy))
- [ ] Setup engine: install, update from a release zip, rollback, uninstall ([ADR-0013](decisions/0013-own-setup-and-update-engine.md)); CLI `gamewarden update` (`--prerelease`, `--from <zip> --allow-unsigned`) and `gamewarden version use`
- [ ] `release.yml`: `Setup.exe`, zip, signed `SHA256SUMS`, attestations; first release candidate through the manual VM checklist ([10 §8](docs/10-build-test-release.md#8-manual-tests-on-the-vm))

**Exit:** GameWarden installed on the VM from a GitHub Release `Setup.exe`; after a VM reboot, `valheim-main` is back up without login; killing the game triggers an automatic restart.

### M2 · Web panel (Owner only)

- [ ] First-run setup wizard (Owner account with the one-time setup token, prerequisites with install, network)
- [ ] Login, session, CSRF, host filtering; listen on all interfaces with the local-address filter (OQ-28)
- [ ] Dashboard, server detail (Overview, Console), new server wizard with resource check
- [ ] Realtime hub: state, job progress, console
- [ ] Visibility: favicon/title status, header heartbeat, connection-lost banner, "Core restarted" notice
- [ ] Update from GitHub Releases in the panel: check, download, verify, apply, roll back ([10 §6](docs/10-build-test-release.md#6-self-update-from-github-releases))

**Exit:** success criterion 1 passes.

### M3 · Backups, schedules, safe updates

- [ ] Backup / restore (extract to a temporary folder, then swap) / prune; pre-operation backups; one configurable backup folder; backup verification (OQ-29); Core state backup (OQ-27); low-disk guard (OQ-30)
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

### M8 · Release polish

Install and self-update arrive in M1–M2 ([ADR-0013](decisions/0013-own-setup-and-update-engine.md)); this milestone finishes the edges.

- [ ] Authenticode signing (SignPath Foundation), so SmartScreen stops warning
- [ ] Pre-release update channel in the panel
- [ ] Uninstall options (keep / remove data)

## Backlog

- Discord login (OQ-04), Player "request restart" (OQ-10)
- HTTPS (would supersede ADR-0005), PWA / push
- Public status page
- One run-as user per instance ([ADR-0008](decisions/0008-run-as-user-and-launch-chain.md))
