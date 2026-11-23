# GameWarden

**A self-hosted web panel for a small group of friends' Steam dedicated game servers — one Windows machine, one
VPN, no command line for daily work.** Valheim first, Palworld second.

[![CI](https://github.com/vinh466/GameWarden/actions/workflows/ci.yml/badge.svg)](https://github.com/vinh466/GameWarden/actions/workflows/ci.yml)
[MIT](LICENSE)

> **Development status (2026-11-23).** GameWarden is pre-1.0 and on a development line (`0.3.0-dev.N`). It is not a
> stable product: there is no support guarantee, and breaking changes are expected. It runs on **one Windows host**
> with **one panel account (the Owner)**; friends' accounts and roles are not built yet. **Backups are not built
> yet** — copy the instance data folder by hand. The panel is **HTTP over a private VPN only** and must never be
> exposed to the internet. The Owner tests releases on a VM; the development line moves forward only.

GameWarden is a tool for people who host games for their friends, not a hosting product. If you need a multi-user
panel for public hosting, use Pterodactyl or Pelican (Linux + Docker) or AMP (paid, closed source).

## What it does

- **Keeps servers in the state you asked for.** A Windows Service starts them after a reboot without anyone
  logging in, restarts them after a crash (with limits), and stops them gracefully so the world is saved.
- **Daily work from one panel.** Install, update, start, stop, restart, read the live console, change settings,
  follow jobs, see CPU / RAM and the activity feed — on a desktop or a phone. A CLI covers the same operations.
- **One adapter per game.** A game is described in JSON plus optional PowerShell hooks. Valheim (L3) and Palworld
  (L2) are built in; adding a game does not change the platform ([04](docs/04-adapter-spec.md)).
- **Valheim mods.** Pinned mod sets from Thunderstore, BepInEx, r2modman import and share codes, and config files
  with revisions ([13](docs/13-mods.md)).
- **Game integrations.** Palworld's REST API (server info, metrics, players, announce, save, kick, ban) and
  Valheim's console players plus `adminlist.txt`, behind one panel tab ([16](docs/16-game-integrations.md),
  [17](docs/17-valheim-console-integration.md)).
- **Discord notifications.** One provider-neutral outbox with instant, digest and live-status messages, test
  button and delivery log ([18](docs/18-discord-notifications.md)).
- **Logs and activity.** A queryable system log, a curated activity feed, and a redacted diagnostics bundle
  ([19](docs/19-system-log.md), [15](docs/15-agent-access.md)).
- **Agent access.** Named Agent clients with scoped tokens, time-boxed diagnostics, audit and a local MCP bridge.
  An in-Core Operations Agent can talk in Discord, read the machine with typed tools and ask for approval before
  every write ([15](docs/15-agent-access.md), [20](docs/20-operations-agent.md)).
- **Safe defaults.** Games, SteamCMD and hooks run as a non-admin Windows user; the panel and game ports are
  reachable only from the VPN; secret values are redacted from logs, and the API returns only whether a secret is
  set.

## What is not built

- **Backups, restore and schedules.** Copy `C:\GameWarden\instances\<id>` or the server's `data` folder by hand.
  The design and the reasons it left the MVP are in [ADR-0018](decisions/0018-backups-leave-the-mvp.md).
- **Friends' accounts, roles, invites and per-server access.** The Owner is the only panel user today; the RBAC
  design and permission list are [06 §5](docs/06-security-and-access.md#5-authorization) and
  [ADR-0006](decisions/0006-rbac-with-separate-owner.md).
- **Terraria, Steam Workshop, an adapter editor and prerequisite installer jobs.**
- **HTTPS, PWA / web push, internet exposure, multiple hosts, Linux or Docker hosts, an in-browser file manager
  and billing.** These are non-goals, not missing pieces ([01 §4](docs/01-vision-and-scope.md#4-non-goals-for-now)).
- **Palworld's VM walk-through, Valheim's console check on the VM, deeper diagnostics and Authenticode signing.**
  The code is built; those checks or release-polish items are next in [ROADMAP.md](ROADMAP.md).

## Scope

| | |
|---|---|
| **Today** | One Windows host, one Owner account, Valheim + Palworld, several servers at once with minimum / recommended resource checks, Valheim mods, Discord notifications, Agent access. |
| **Next** | Friends' accounts and roles (M6), backups / restore / schedules (backlog), Palworld VM walk-through, deeper diagnostics and runbooks, Steam Workshop, Terraria. |
| **Not planned** | Several hosts or clustering · Linux or Docker hosts · exposing the panel to the internet · hosting for customers or billing · moving backups off the machine · an in-browser file manager. |

The plan and exit criteria are in [ROADMAP.md](ROADMAP.md); the product boundaries are
[01-vision-and-scope](docs/01-vision-and-scope.md) and [ADR-0011](decisions/0011-mvp-scope-and-direction.md).

## Who it is for

- You have a Windows PC or VM that stays on. The verified target is a Windows 10 IoT Enterprise LTSC 2021 VM;
  other Windows 10 / 11 editions are expected to work but are not verified.
- You and your friends share a VPN: **Radmin VPN** or **Tailscale**.
- You want the server to come back after a reboot or a crash, and you accept copying saves by hand until backups
  are built.

## Install on a Windows VM (development release)

1. From [Releases](https://github.com/vinh466/GameWarden/releases), download `GameWarden-Setup-<version>.exe` and
   run it. Windows asks for administrator rights, and SmartScreen warns because the binaries are not code-signed
   yet (*More info* → *Run anyway*). `SHA256SUMS` and the build attestations let you check the download.
2. Open a **new terminal as Administrator** and run `gamewarden setup-url`; open the link it prints in a browser
   on the VM and create the Owner account. The wizard shows the prerequisites (install the Microsoft Visual C++
   x64 runtime if it is missing) and whether the download hosts resolve. If one is blocked, your ISP's DNS hides
   it (seen in Vietnam), so set the VM's DNS to `1.1.1.1` ([08 §2](docs/08-operations.md#2-prerequisites)).
3. In the panel — on the VM at `http://localhost:8765`, or from another PC or a phone on the VPN at
   `http://<vpn-ip>:8765` — open *Servers → New server*, pick Valheim, give it a name, world and password, and
   *Create & install*; then *Start*. Friends join over the VPN at `<vpn-ip>:2456`.

Mods and the same operations from the command line:

```powershell
gamewarden create valheim-main --adapter valheim --set serverName=MyServer --set world=MyWorld
gamewarden secret valheim-main password
gamewarden install valheim-main --wait
gamewarden start valheim-main

gamewarden mods import valheim-main <code>          # preview the mod set; changes nothing
gamewarden mods import valheim-main <code> --yes --wait
gamewarden mods on valheim-main
gamewarden mods share valheim-main                  # a code friends import in r2modman
```

Updates: *System → Check for updates* in the panel, or `gamewarden update`. Read the release notes first: they say
whether data is kept and what to back up. `gamewarden help` lists every command. The manual test passes are in
[10 §8](docs/10-build-test-release.md#8-manual-tests-on-the-vm).

Do not run `Setup.exe` on the development machine: it installs a service and a local user. The VM is the test
environment.

## How it is built

| Part | What it is |
|---|---|
| **Core** | C# / .NET 10 Windows Service (`GameWardenCore`): state, jobs, auth, API, panel hosting, notifications, the Operations Agent. |
| **Runner** | One small process per running game server; owns the game console and survives Core restarts. |
| **Panel** | React + TypeScript SPA (Mantine, TanStack Router and Query, generated API client), served by Core; English and Vietnamese. |
| **CLI** | `gamewarden` over the loopback API, for setup, recovery and scripting. |
| **Adapters** | Per-game JSON and optional PowerShell hooks, at support levels L0–L3. |
| **Agents** | Scoped external Agent clients over `/api/v1/agent` plus a local MCP bridge; Discord for the in-Core Operations Agent. |

Architecture: [02](docs/02-architecture.md). Decisions behind these choices: [decisions/](decisions/).

## Development

Requirements: any OS with the .NET SDK pinned in [global.json](global.json), and Node.js 24 for the panel. The
Owner develops on Ubuntu ([ADR-0017](decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md)). Admin
rights are not needed.

On Linux, Core runs on the development platform ([ADR-0016](decisions/0016-windows-as-an-integration-layer.md)).
It is not a deployment target: there is no isolation, firewall, service, self-update or SteamCMD download, and
secrets, tokens and session keys are protected by file permissions only.

```bash
dotnet build GameWarden.slnx                  # also writes panel/openapi/gamewarden-api.json
dotnet test --solution GameWarden.slnx        # Unit, Api and Integration; Windows tests skip themselves elsewhere
node build/check-docs.mjs                     # Markdown links, Excalidraw diagrams, unique ADR numbers

# Core in console mode with a development data root (~/.local/share/GameWardenDev, port 8765):
dotnet run --project src/GameWarden.Core --launch-profile "Core (development)"

# In a second terminal, the CLI against it; it reads the development token by default:
dotnet run --project src/GameWarden.Cli -- create fake-1 --adapter fakegame --set executable="$PWD/tests/FakeGame/bin/Debug/net10.0/FakeGame"
dotnet run --project src/GameWarden.Cli -- start fake-1
dotnet run --project src/GameWarden.Cli -- status
```

- **Another port** (the dev box is shared): `GameWarden__PanelPort=28765` for Core,
  `GAMEWARDEN_URL=http://127.0.0.1:28765` for the CLI and the panel's dev server.
- **On Windows** the same commands work in PowerShell, and the `Windows` tests run too. The development token is
  `%LOCALAPPDATA%\GameWardenDev\cli.token`, so set `GAMEWARDEN_CLI_TOKEN` to it; FakeGame is `FakeGame.exe`.
- **Panel only:** `cd panel && npm ci && npm run dev:mock` runs it without Core. The rest is in
  [panel/CONTRIBUTING.md](panel/CONTRIBUTING.md).

Release assets (zip, `Setup.exe`, `SHA256SUMS`) for a local version, on Windows:
`./build/package.ps1 -Suffix dev.local` → `artifacts\`.

What needs admin rights, a reboot, a real game or the VM is tested by hand on the VM
([10 §8](docs/10-build-test-release.md#8-manual-tests-on-the-vm)).

## Documentation

The full map — by task, for readers and coding agents — is [docs/README.md](docs/README.md). Short entry points:

| If you want to… | Start with |
|---|---|
| Understand the product and its boundaries | [01-vision-and-scope](docs/01-vision-and-scope.md), [ROADMAP](ROADMAP.md) |
| Understand how it works | [02-architecture](docs/02-architecture.md), [diagrams](docs/diagrams/README.md) |
| Install or operate it | [08-operations](docs/08-operations.md), [10-build-test-release](docs/10-build-test-release.md) |
| Work on the panel | [11-web-panel](docs/11-web-panel.md), [07-ux-and-user-flows](docs/07-ux-and-user-flows.md), [22-panel-ux-review](docs/22-panel-ux-review.md) |
| Work on security, roles or agents | [06-security-and-access](docs/06-security-and-access.md), [23-users-and-roles](docs/23-users-and-roles.md), [15-agent-access](docs/15-agent-access.md) |
| Know why something is the way it is | [decisions/](decisions/) (ADRs) |
| Work in this repository as an agent | [CLAUDE.md](CLAUDE.md), then [docs/README.md](docs/README.md#for-coding-agents) |

Docs state their own status at the top. A doc that describes a design says what is **not built yet** before the
design; the code is the source of truth for what exists.

## Layout

```
GameWarden/
├─ README.md                 you are here
├─ ROADMAP.md                milestones and exit criteria
├─ CLAUDE.md                 working rules for coding agents
├─ AGENTS.md                 pointer for agent tools that look for this file
├─ docs/                     the specification (01–23), diagrams, UX evidence, release notes
├─ decisions/                ADRs: why things are the way they are
├─ spikes/                   small experiments behind decisions (not shipped)
├─ src/                      Core · Runner · CLI · Contracts · Setup · MCP
├─ tests/                    FakeGame test double · Core.Tests · Runner.Tests · Shared
├─ adapters/                 built-in adapters: fakegame, valheim, palworld
├─ panel/                    React panel and its API client
└─ build/                    docs check, packaging, signing helpers
```

## Conventions

- **Facts vs assumptions.** Anything not verified is tagged **[Unverified]**. Measured facts carry the date they
  were measured.
- **Dates** are absolute (`YYYY-MM-DD`). **MUST / SHOULD / MAY** in specs follow RFC 2119.
- **Secrets are never committed** (passwords, webhook URLs, tokens). The repository is public.
- **New decision → new ADR** (copy [decisions/0000-template.md](decisions/0000-template.md)). Do not rewrite
  accepted ADRs; supersede them. Take the next number from a fresh `origin/main`.
- **Docs match the code.** A doc or ROADMAP item says "built" only with the behaviour and the pull request number.
- Work on a branch and open a pull request, docs-only changes too; never push to `main`.

## License

[MIT](LICENSE).

## Acknowledgements

Pelican eggs, LinuxGSM and WindowsGSM serve as references for launch parameters, config formats and per-game
quirks.
