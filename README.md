# GameWarden

Self-hosted web panel for running **Steam dedicated game servers on one Windows machine**, for **a small group of friends** who connect over a VPN. Valheim first.

> **Status (2026-10-28): M2 release candidate — the web panel, for testing on a VM.** Setup installs a Windows service that installs, runs, restarts and updates a Valheim server as a non-admin user and updates itself from GitHub Releases; the Owner runs it from a web panel over the VPN (live state, console, CPU / RAM, activity, updates) or from the command line. Mods for Valheim come next, then Palworld; backups and Discord wait in the backlog ([ADR-0018](decisions/0018-backups-leave-the-mvp.md), [ADR-0019](decisions/0019-discord-joins-the-backlog-mods-next.md)), so copy a server's `data` folder by hand. The plan is in [ROADMAP.md](ROADMAP.md).

## The problem

Hosting a dedicated server for friends on Windows usually means: run SteamCMD by hand, edit config files, keep a console window open, remember to copy the save folder, and find out yourself when the server crashed at 1 a.m. Every game does each of these differently.

Existing tools did not fit this case ([ADR-0001](decisions/0001-build-own-platform.md)):

- **WindowsGSM** — the servers did not start on our VM (2026-10-27), and maintenance is sporadic.
- **AMP** — mature, but closed source and paid per instance tier.
- **Pterodactyl / Pelican** — Linux and Docker only.

## What GameWarden does

- **Keeps servers in the state you asked for.** A Windows Service starts them after a reboot without anyone logging in, restarts them after a crash (with limits), and stops them gracefully so the world is saved.
- **One web panel** to install, update, start, stop, watch the live console, back up and restore. No command line.
- **One adapter per game.** A game is described in JSON, plus optional PowerShell hooks; adding a game does not change the platform.
- **Backups you can rely on.** Scheduled, and taken automatically before every risky operation; all collected in one folder.
- **Discord notifications** through a webhook: crashes, updates, and a live status message.
- **Safe defaults.** Games, mods and hooks run as a non-admin Windows user; the panel and the game ports are reachable only from the VPN.

## Scope

| | |
|---|---|
| **MVP** | One Windows machine, **one panel user (the Owner)**, Valheim. Several servers at once, with minimum / recommended resource checks before each start. Backups collected in one folder. Discord webhook. |
| **After the MVP** | Friends with accounts and roles (Admin / Operator / Player, per server), settings forms, more games (Palworld, Terraria, ...), mods (Thunderstore, Steam Workshop), an installer. |
| **Not planned** | Several hosts or clustering · Linux or Docker hosts (use Pelican / Pterodactyl) · exposing the panel to the internet · hosting for customers or billing · moving backups off the machine (copy the backup folder with any tool you like) · an in-browser file manager. |

GameWarden is a tool for people who host games for their friends, not a hosting product. Details: [01-vision-and-scope](docs/01-vision-and-scope.md), [ADR-0011](decisions/0011-mvp-scope-and-direction.md).

## Who it is for

- You have a Windows PC or VM that stays on. The primary target is a Windows 10 IoT Enterprise LTSC 2021 VM; other Windows 10 / 11 editions are expected to work but are not verified yet.
- You and your friends share a VPN: **Radmin VPN** or **Tailscale**.
- You want the server to just be up, with saves that never get lost.

## At a glance

| Concern | Choice | Details |
|---|---|---|
| Scope | MVP for one Owner and Valheim; friends' accounts and roles later | [ADR-0011](decisions/0011-mvp-scope-and-direction.md) |
| Host | One Windows VM (template, scaled up as needed) | [01-vision-and-scope](docs/01-vision-and-scope.md) |
| Core | C# / .NET 10, runs as a Windows Service | [ADR-0002](decisions/0002-core-as-csharp-windows-service.md) |
| Process control | One small *Runner* process per game server | [ADR-0003](decisions/0003-runner-process-per-instance.md) |
| Process identity | Games, SteamCMD and hooks run as the non-admin user `gamewarden-run`, started with `CreateProcessAsUser` | [ADR-0008](decisions/0008-run-as-user-and-launch-chain.md) |
| Data root and secrets | Explicit ACL on `C:\GameWarden`; secrets readable only by SYSTEM (DPAPI, CurrentUser scope) | [ADR-0009](decisions/0009-data-root-acl-and-secrets.md) |
| UI | Web panel (React + TypeScript SPA) served by Core; no tray app | [ADR-0004](decisions/0004-web-panel-only-no-tray.md), [ADR-0010](decisions/0010-panel-react-typescript.md) |
| Access | HTTP only, reachable only over the VPN (Radmin or Tailscale) | [ADR-0005](decisions/0005-http-only-over-vpn.md) |
| Users | MVP: the Owner. Later: friends with roles (Admin / Operator / Player) | [ADR-0006](decisions/0006-rbac-with-separate-owner.md) |
| Game support | Per-game adapters: JSON + optional PowerShell hooks, levels L0–L3 | [ADR-0007](decisions/0007-declarative-adapters-with-hooks.md) |
| Build and release | GitHub Actions on hosted runners; `Setup.exe` on GitHub Releases; self-update with rollback; Windows-bound behaviour tested by hand on the VM | [10-build-test-release](docs/10-build-test-release.md), ADR-0013, ADR-0014, [ADR-0017](decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md) |
| Platform layer | Portable Core and Runner; Windows behind adapters; development and tests on Linux, Windows checked by CI and the VM | [ADR-0016](decisions/0016-windows-as-an-integration-layer.md), [ADR-0017](decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md) |
| Why not WindowsGSM / AMP | Build own platform | [ADR-0001](decisions/0001-build-own-platform.md) |

## Names

| Thing | Name |
|---|---|
| Product | GameWarden |
| Windows service | `GameWardenCore` |
| CLI | `gamewarden` |
| Default data root | `C:\GameWarden` |
| Default panel URL | `http://<vpn-ip>:8765` |

## Layout

```
GameWarden/
├─ README.md                      you are here
├─ ROADMAP.md                     milestones + exit criteria (MVP = M0–M2)
├─ docs/
│  ├─ 01-vision-and-scope.md      problem, goals, non-goals, personas, constraints, glossary
│  ├─ 02-architecture.md          components, Core modules, runner, process model, tech stack
│  ├─ 03-domain-model.md          entities, instance state machine, jobs, events
│  ├─ 04-adapter-spec.md          adapter format, settings schema, hooks, mods, examples
│  ├─ 05-api.md                   REST + realtime API, auth, errors
│  ├─ 06-security-and-access.md   threat model, network, RBAC, secrets, audit
│  ├─ 07-ux-and-user-flows.md     lifecycle, screens, flows, "is it running" visibility
│  ├─ 08-operations.md            filesystem, prerequisites, install/upgrade, backups, logs
│  ├─ 09-open-questions.md        open and closed questions, spikes
│  ├─ 10-build-test-release.md    CI, test strategy, release assets, install, self-update
│  ├─ 11-web-panel.md             web panel (M2): functions, layout, style, stack, working rules
│  ├─ 12-explanations.md          help drawer: longer explanations with sources, for settings and choices
│  └─ diagrams/                   Excalidraw: context, modules, processes, ACL, states, flows
├─ decisions/                     ADRs (why things are the way they are)
├─ spikes/                        small experiments behind decisions (not shipped)
├─ src/                           GameWarden.Contracts · .Runner · .Core · .Cli · .Setup
├─ tests/                         FakeGame (test double) · Runner.Tests · Core.Tests
├─ adapters/                      built-in adapters (fakegame, valheim)
└─ build/                         docs check, package.ps1 (release assets), sign-manifest.ps1
```

## Install on a Windows VM (release candidate)

1. From [Releases](https://github.com/vinh466/GameWarden/releases), download `GameWarden-Setup-<version>.exe` and run it. Windows asks for administrator rights, and SmartScreen warns because the binaries are not code-signed yet (*More info* → *Run anyway*). `SHA256SUMS` and the build attestations let you check the download.
2. Open a **new terminal as Administrator** and run `gamewarden setup-url`; open the link it prints in a browser on the VM and create the Owner account. The next step shows the prerequisites (install the Microsoft Visual C++ x64 runtime if it is missing) and whether the download hosts resolve: if one is blocked, your ISP's DNS hides it (seen in Vietnam), so set the VM's DNS to `1.1.1.1` ([08 §2](docs/08-operations.md#2-prerequisites)).
3. In the panel — on the VM at `http://localhost:8765`, or from another PC or a phone on the VPN at `http://<vpn-ip>:8765` — open *Servers → New server*, pick Valheim, give it a name, world and password, and *Create & install*; then *Start*. The same from the command line:

   ```powershell
   gamewarden create valheim-main --adapter valheim --set serverName=MyServer --set world=MyWorld
   gamewarden secret valheim-main password
   gamewarden install valheim-main --wait
   gamewarden start valheim-main
   gamewarden status
   ```

4. Friends join over the VPN at `<vpn-ip>:2456`. The firewall rule GameWarden adds allows only the VPN ranges (Radmin `26.0.0.0/8`, Tailscale `100.64.0.0/10`).

Updates: *System → Check for updates* in the panel, or `gamewarden update --prerelease` for release candidates. `gamewarden help` lists every command. The test passes: [10 §8.1](docs/10-build-test-release.md#81-walk-through-for-an-m1-release-candidate-cli-only) (CLI) and [10 §8.3](docs/10-build-test-release.md#83-walk-through-for-panel-access-m2-phase-2) (panel).

## Development

Requirements: any OS with the .NET SDK pinned in [global.json](global.json), and Node.js 24 for the panel. The Owner develops on Ubuntu ([ADR-0017](decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md)). Admin rights are not needed.

On Linux, Core runs on the development platform ([ADR-0016](decisions/0016-windows-as-an-integration-layer.md)). It is not a deployment target:

- there is no isolation, firewall, service, self-update or SteamCMD download;
- secrets, tokens and session keys are protected by file permissions only.

```bash
dotnet build GameWarden.slnx                  # also writes panel/openapi/gamewarden-api.json
dotnet test --solution GameWarden.slnx        # Unit, Api and Integration; Windows tests skip themselves elsewhere

# Core in console mode with a development data root (~/.local/share/GameWardenDev, port 8765):
dotnet run --project src/GameWarden.Core --launch-profile "Core (development)"

# In a second terminal, the CLI against it; it reads the development token (~/.local/share/GameWardenDev/cli.token) by default:
dotnet run --project src/GameWarden.Cli -- system
dotnet run --project src/GameWarden.Cli -- create fake-1 --adapter fakegame --set executable="$PWD/tests/FakeGame/bin/Debug/net10.0/FakeGame"
dotnet run --project src/GameWarden.Cli -- start fake-1
dotnet run --project src/GameWarden.Cli -- status
```

- **Another port** (the dev box is shared): `GameWarden__PanelPort=28765` for Core, `GAMEWARDEN_URL=http://127.0.0.1:28765` for the CLI and the panel's dev server.
- **On Windows** the same commands work in PowerShell, and the `Windows` tests run too. The development token is `%LOCALAPPDATA%\GameWardenDev\cli.token`, so set `GAMEWARDEN_CLI_TOKEN` to it; FakeGame is `FakeGame.exe`.

The web panel: `cd panel && npm ci && npm run dev:mock` runs it without Core; the rest is in [panel/CONTRIBUTING.md](panel/CONTRIBUTING.md).

Release assets (zip, `Setup.exe`, `SHA256SUMS`) for a local version, on Windows: `./build/package.ps1 -Suffix dev.local` → `artifacts\`. Don't run the Setup on the development machine: it installs a service and a user.

What needs admin rights, a reboot, a real game or the VM is tested by hand on the VM ([10 §8](docs/10-build-test-release.md#8-manual-tests-on-the-vm)).

## Reading order

1. [01-vision-and-scope](docs/01-vision-and-scope.md)
2. [02-architecture](docs/02-architecture.md) with diagrams 01–04 ([docs/diagrams](docs/diagrams/)), and [decisions/](decisions/)
3. [07-ux-and-user-flows](docs/07-ux-and-user-flows.md) with flow diagrams 06–09
4. [03-domain-model](docs/03-domain-model.md) with diagram 05, [04-adapter-spec](docs/04-adapter-spec.md), [05-api](docs/05-api.md)
5. [06-security-and-access](docs/06-security-and-access.md), [08-operations](docs/08-operations.md)
6. [10-build-test-release](docs/10-build-test-release.md) with diagram 10
7. [09-open-questions](docs/09-open-questions.md) and [ROADMAP](ROADMAP.md)

## Conventions

- **Facts vs assumptions.** Anything not verified is tagged **[Unverified]**. Measured facts carry the date they were measured.
- **Dates** are absolute (`YYYY-MM-DD`).
- **MUST / SHOULD / MAY** in specs follow RFC 2119 meaning.
- **Secrets are never committed** (passwords, webhook URLs, tokens). The repository is public.
- **New decision → new ADR** (copy [decisions/0000-template.md](decisions/0000-template.md)). Don't rewrite accepted ADRs; supersede them.
- Docs are written in English.

## License

[MIT](LICENSE).

## Acknowledgements

Pelican eggs, LinuxGSM and WindowsGSM serve as references for launch parameters, config formats and per-game quirks.
