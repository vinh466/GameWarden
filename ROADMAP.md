# Roadmap

Each milestone ends with something usable and has explicit exit criteria. Estimates are deliberately omitted until M0 is done.

**MVP = M0, M1 and M2:** one Owner runs Valheim unattended on the Windows VM from a web panel ([ADR-0011](decisions/0011-mvp-scope-and-direction.md)). Backups (M3) and Discord (M4) moved to the backlog on 2026-10-30 ([ADR-0018](decisions/0018-backups-leave-the-mvp.md), [ADR-0019](decisions/0019-discord-joins-the-backlog-mods-next.md)); until backups come back the Owner backs up by hand. Later milestones build on the MVP without changing its contracts. Success criteria: [01 §8](docs/01-vision-and-scope.md#8-success-criteria) (criterion 3's Discord message and criterion 4 wait in the backlog).

**Next** ([ADR-0019](decisions/0019-discord-joins-the-backlog-mods-next.md)): mods for Valheim (M7's Thunderstore provider), then Palworld (M5).

## MVP

### M0 · Spikes and prerequisites

- [ ] Run spikes S1–S8 from [09-open-questions](docs/09-open-questions.md#spikes-needed-before-m1-answers-feed-back-into-the-spec) (S1 part 1 and S6 answered 2026-10-27)
- [x] Close OQ-01 (panel framework → [ADR-0010](decisions/0010-panel-react-typescript.md)) and OQ-02 (run-as user → [ADR-0008](decisions/0008-run-as-user-and-launch-chain.md))
- [x] Close OQ-07, OQ-08, OQ-09, OQ-13 to OQ-23 (2026-10-27, [ADR-0011](decisions/0011-mvp-scope-and-direction.md) and [09](docs/09-open-questions.md))
- [x] Close OQ-03 (port 8765) and OQ-24 (MIT license)
- [x] Public GitHub repository
- [x] CI skeleton ([ADR-0012](decisions/0012-ci-and-tests-on-github-hosted-runners.md)): `ci.yml` (docs job + .NET job on `windows-2025`), Dependabot, CodeQL, `SECURITY.md`; the panel job comes with M2
- [ ] Branch protection on `main` (repository setting)
- [x] Dev machine: .NET SDK 10.0.403 on the Owner's Windows 11 PC ([02 §11](docs/02-architecture.md#11-development-environment)); Node.js comes with the panel (M2); the game VM stays clean
- [ ] Update the spec with spike results (remove `[Unverified]` where confirmed)

**Exit:** Valheim started and stopped (with save) from a service through a prototype Runner on the template VM.

### M1 · Core skeleton + Valheim L1 (CLI only)

- [x] Solution layout (`Core`, `Runner`, `Cli`, `Contracts`, tests)
- [x] SQLite + EF Core migrations, data root layout
- [x] Windows Service host with pre-shutdown ([ADR-0014](decisions/0014-service-host-with-preshutdown.md)); console mode stays for development
- [x] `gamewarden-run` user, data-root ACL, launch chain ([ADR-0008](decisions/0008-run-as-user-and-launch-chain.md), [ADR-0009](decisions/0009-data-root-acl-and-secrets.md)); on when Core runs as the service, off in development and CI
- [x] Adapter loader and validation (built-in read-only, Owner adapters override by id — OQ-18); `fakegame` dev adapter; `valheim` adapter at L1
- [ ] JSON Schema file for `adapter.json` (editor support)
- [x] SteamCMD bootstrap + install/update jobs with the instance lock and the global SteamCMD lock (a busy instance answers 409 instead of queueing)
- [x] Runner: console rules, Job Object, ACL'd pipe, stop methods, exit record ([02 §4](docs/02-architecture.md#4-process-model-and-the-runner))
- [x] Supervisor: Runner start/attach/stop, readiness (`logRegex` from stdout or a file, `delay`, `port`), restart policy, reconciliation; one queue per instance (OQ-25); pipe handshake + PID check (OQ-26); one Core per machine (OQ-31)
- [x] Port allocation + firewall rules; resource checks (min / recommended); secrets (`{secrets.*}`)
- [x] CLI: `status`, `start`, `stop`, `restart`, `logs`, plus `create`, `console`, `adapters`, `settings`, `set`, `secret`, `install`, `update-game`, `jobs`, `job`, `system`, `delete --purge`
- [x] Prerequisite checks (detect only)
- [x] `FakeGame`; `Unit`, `Api`, `Windows` tests in CI ([10 §2–3](docs/10-build-test-release.md#2-test-strategy))
- [x] Dev-build artifact for every push to `main`
- [x] Setup engine: install, update from a release zip, rollback, uninstall ([ADR-0013](decisions/0013-own-setup-and-update-engine.md)); CLI `gamewarden update` (`--prerelease`, `--from <zip> --allow-unsigned`) and `gamewarden version use`
- [x] `release.yml`: `Setup.exe`, zip, signed `SHA256SUMS`, attestations
- [x] First release candidate through the manual VM walk-through, steps 1–7 ([10 §8.1](docs/10-build-test-release.md#81-walk-through-for-an-m1-release-candidate-cli-only)): passed with v0.1.0-rc.1 on 2026-10-28
- [x] Walk-through step 8: self-update from one release candidate to the next and back (`gamewarden update --prerelease`, `gamewarden version use`): passed rc.1 → rc.2 → rc.1 → rc.2 on 2026-10-28
- [ ] Walk-through step 9: uninstall (optional on the Owner's VM; run before the first non-RC release)

**Exit:** GameWarden installed on the VM from a GitHub Release `Setup.exe`; after a VM reboot, `valheim-main` is back up without login; killing the game triggers an automatic restart. **Met on 2026-10-28 with v0.1.0-rc.1.**

### M2 · Web panel (Owner only)

Approved design: [11-web-panel](docs/11-web-panel.md), [ADR-0015](decisions/0015-panel-mantine-and-toolchain.md) (Mantine 9, TanStack Router and Query, generated API client; English and Vietnamese).

- [x] Phase 1 — panel project, theme, AppShell, every screen with mock data; review of look and layout on desktop and phone (#8)
- [x] Phase 2 — Core access (#10): Owner account, first-run setup with the one-time token, sessions, CSRF, login throttling; listen on all interfaces with the local-address filter (OQ-28) and host filtering; panel firewall rule; static hosting; typed API results
- [x] Phase 3 — live data (#13, #14): generated client, realtime hub (state, job progress, console), CPU / RAM per server and VM, activity feed
- [x] Phase 4 — new server wizard with resource, prerequisite and DNS checks; System page with update and rollback from GitHub Releases ([10 §6](docs/10-build-test-release.md#6-self-update-from-github-releases)); adapters (#15)
- [x] Phase 5 — command palette, notifications, favicon / title status, connection-lost banner, "Core restarted" notice; accessibility and phone pass; tests; release candidate through the VM test over Radmin and from a phone (#16): `v0.2.0-rc.1` passed on 2026-10-28

**Exit:** success criterion 1 passes.

### Platform layer and Linux development (before M3, at the Owner's request)

Development moved to Linux on 2026-10-29 ([ADR-0017](decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md)); Windows becomes an integration layer behind ports ([ADR-0016](decisions/0016-windows-as-an-integration-layer.md)).

- [x] Decisions: ADR-0016, ADR-0017; docs 02 §11, 10 §2–3 and §7 (#20)
- [x] Portable Core: `net10.0`, `PlatformServices`, ports `IMachine` and `IPrerequisiteChecker`, development platform for secrets, token files and keys; Core runs and the OpenAPI document builds on Linux; `dotnet-linux` CI job (#21)
- [x] Portable Runner: console, child-process and pipe adapters for Windows and Linux; `Integration` tests and the e2e smoke test run on Linux (#21)
- [ ] VM check of the moved Windows adapters with `v0.2.0-rc.3` (update or fresh install): 10 §8.1 steps 2, 4, 5 and §8.3 sign-in (ADR-0017 §5)

**Exit:** on the Linux dev box, `dotnet build` and `dotnet test` pass without flags, and Core runs a `fakegame` server that the panel's dev server controls; on `windows-2025` every tier, including `Windows`, passes. **Met on 2026-10-29** (#21): Linux 196 tests, 183 passed and 13 Windows-only skipped; `windows-2025` 186 passed and 10 Linux-only skipped; the panel's dev server created, started and stopped a `fakegame` server on Linux.

## After the MVP

### M5 · Settings forms + more games

- [x] Settings schema → forms (wizard and Settings tab, one section per group), renderer `arg`: done 2026-10-29, before M3 at the Owner's request ([04 §5](docs/04-adapter-spec.md#5-settingsschemajson-l2))
- [x] Settings help: a drawer per setting with a longer explanation and its sources, one pattern reused across the panel (before M3, at the Owner's request): done 2026-10-29 ([12](docs/12-explanations.md)); Valheim has 19 topics since 0.3.2, the panel 9; `-public` as a setting since 0.3.2, on by default since 0.3.4 (OQ-38)
- [ ] Renderers `ini`, `hook` first, then `json`, `keyvalue`, `env`; setting type `number`
- [x] `valheim` L2: every launch argument as a setting, crossplay with a warning (2026-10-29, [04 §9](docs/04-adapter-spec.md#9-example-valheim-l2)); VM walk-through [10 §8.4](docs/10-build-test-release.md#84-walk-through-for-valheim-settings-l2) passed on 2026-10-29 (the details for OQ-35 and OQ-36 not recorded)
- [ ] `palworld` L2 (renderConfig hook): **next after Valheim mods** ([ADR-0019](decisions/0019-discord-joins-the-backlog-mods-next.md)); `terraria` L1–L2
- [ ] L0 guide rendering

### M6 · Friends: accounts, roles, audit

- [ ] Permissions + roles + scoped bindings ([ADR-0006](decisions/0006-rbac-with-separate-owner.md))
- [ ] Invites, user management
- [ ] Audit page + activity feed with user names
- [ ] Role-aware UI
- [ ] Share button (connect info)

**Exit:** success criterion 6 passes.

### M7 · Mods (L3)

- [ ] Thunderstore provider (Valheim / BepInEx): **next** ([ADR-0019](decisions/0019-discord-joins-the-backlog-mods-next.md)), from a design document the Owner approves first ([13](docs/13-mods.md), [ADR-0020](decisions/0020-mods-in-set-folders-as-pinned-sets.md), accepted 2026-10-30): phases 1a, 1b, 1c, 2 ([13 §6](docs/13-mods.md#6-plan))
  - [x] 1a: mod sets, Thunderstore client, resolver, `mods.sync` (2026-10-30)
  - [x] 1b: BepInEx loader (S-M2), Doorstop entry files, *Mods on*, config carry-over, Valheim adapter 0.4.0 at L3 (2026-10-30); the VM walk-through [10 §8.5](docs/10-build-test-release.md#85-mods-phase-1b) is next
  - [x] 1c: r2modman codes both ways (preview and import, share code), configs from a code and viewable, update check, `gamewarden mods` (2026-10-30); the VM walk-through with the group's code ([10 §8.5](docs/10-build-test-release.md#85-mods-phase-1b), S-M3) is next
  - [x] 2: Mods tab, Thunderstore search, BepInEx log (2026-10-30); the VM walk-through (Epic Loot and Norsemen, a friend joins with the share code) is next
- [ ] Steam Workshop provider
- [ ] Mods tab, mod list in Share text

### M8 · Release polish

Install and self-update arrive in M1–M2 ([ADR-0013](decisions/0013-own-setup-and-update-engine.md)); this milestone finishes the edges.

- [ ] Authenticode signing (SignPath Foundation), so SmartScreen stops warning
- [ ] Pre-release update channel in the panel
- [ ] Uninstall options (keep / remove data)

## Backlog

### Backups, schedules, safe updates (was M3)

In the backlog since 2026-10-30, at the Owner's request ([ADR-0018](decisions/0018-backups-leave-the-mvp.md)): the games keep their own backups and the Owner copies save folders by hand. ADR-0018 lists what the readiness review of 2026-10-29 found, to start from when this comes back: restore needs a new permission design first.

- [ ] Backup / restore (extract to a temporary folder, then swap) / prune; pre-operation backups; one configurable backup folder; backup verification (OQ-29); Core state backup (OQ-27); low-disk guard (OQ-30)
- [ ] Scheduler (backup, restart, update-check) in the VM's local time
- [ ] Update flow as one composite job
- [ ] Archive / purge instance

**Exit:** success criterion 4 passes.

### Discord notifications and monitoring (was M4)

In the backlog since 2026-10-30, at the Owner's request ([ADR-0019](decisions/0019-discord-joins-the-backlog-mods-next.md)): first the features the group needs, then Discord in one pass. Player probes or `/healthz` can come out of the backlog on their own when mods or Palworld need them.

- [ ] Discord webhook events + one live status message edited in place (pinned once by hand)
- [ ] `/healthz`, Windows Event Log entries
- [ ] Player probes (A2S / log / REST)

**Exit:** success criterion 3 passes with its Discord message.

### Other ideas

- Discord login (OQ-04), Player "request restart" (OQ-10)
- HTTPS (would supersede ADR-0005), PWA / push
- Public status page
- One run-as user per instance ([ADR-0008](decisions/0008-run-as-user-and-launch-chain.md))
