# 02 · Architecture

## 1. Context

```
   Friends' / Owner's browsers                 Discord
   (PC, phone — over Tailscale or Radmin)        ▲ webhook
                 │ HTTP + WebSocket               │
                 ▼                                │
┌──────────────────────── Windows VM ────────────────────────────────┐
│                                                                    │
│  GameWardenCore (Windows Service, LocalSystem — see §6)            │
│    ├─ serves the web panel (static files)                          │
│    ├─ REST API + realtime hub                                      │
│    └─ manages ──► Runner ──► game server process  (one per instance)│
│                  Runner ──► game server process                    │
│                                                                    │
│  gamewarden CLI ──(loopback API)──► Core                           │
│  SteamCMD  ◄── started by Core jobs                                │
│  C:\GameWarden\  adapters · instances · backups · db · logs        │
└────────────────────────────────────────────────────────────────────┘
          ▲ game traffic (UDP/TCP) only from VPN address ranges
```

## 2. Components

| Component | Kind | Responsibility |
|---|---|---|
| **Core** (`GameWarden.Core`) | Windows Service, ASP.NET Core host | All state and decisions: instances, jobs, scheduling, auth, API, panel hosting. |
| **Runner** (`GameWarden.Runner`) | Small console exe, one per running instance | Starts the game process, owns its console (stdin/stdout, Ctrl+C), streams output, survives Core restarts. |
| **CLI** (`gamewarden`) | Console exe | Status and emergency operations over the loopback API (§8). |
| **Web panel** (`GameWarden.Web`) | SPA, static files served by Core | All user interaction. Framework is an open question (OQ-01). |
| **Adapters** | Data + scripts on disk | Per-game knowledge. See [04-adapter-spec](04-adapter-spec.md). |
| **SteamCMD** | External tool | Downloads/updates game server files. Core runs **one SteamCMD at a time**. |

## 3. Core modules

| Module | Responsibility |
|---|---|
| **Api** | REST endpoints, auth middleware, antiforgery, ProblemDetails errors. |
| **Realtime** | SignalR hub: pushes events (status, job progress, console lines) to authorized clients. |
| **Identity** | Users, password hashing, sessions, invites, role bindings, permission checks. |
| **Instances** | CRUD of instances, desired state, port allocation, settings rendering. |
| **Supervisor** | Talks to Runners; start/stop/kill; readiness and crash detection; restart policy; reconciliation loop. |
| **Jobs** | Queue and execute long operations; progress + log; cancellation; per-instance locks; global SteamCMD lock. |
| **Scheduler** | Cron-like triggers: backups, restarts, update checks. Creates jobs. |
| **Adapters** | Load, validate and version adapters; expose schemas to the panel; run hooks. |
| **Steam** | SteamCMD bootstrap, install/update, build-id check, output parsing for progress. |
| **Backups** | Create/restore/prune archives; pre-operation safety backups. |
| **Prereqs** | Detect and install VC++ runtime, DirectX runtime, SteamCMD; disk/port checks. |
| **Firewall** | Create/remove Windows Firewall rules per instance port, scoped to VPN ranges. |
| **Notifications** | Event subscribers: Discord webhook (messages + pinned status message). |
| **Audit** | Append-only record of user actions. |
| **Storage** | SQLite (EF Core) + file layout under the data root. |

Modules communicate through an in-process **event bus**; notification, audit and realtime push are subscribers, so new outputs never change the modules that raise events.

## 4. Process model and the Runner

### 4.1 Why a Runner

- If Core launched games directly, restarting or upgrading Core would either kill the games or orphan them with no way to send `save`/`exit` to their stdin.
- Each Runner gets **its own hidden console**, which makes Ctrl+C delivery possible (§4.3).

### 4.2 Lifecycle

```
Core ──CreateProcess(detached)──► Runner(instanceId, launchSpec.json)
                                   ├─ creates Job Object (KILL_ON_JOB_CLOSE)
                                   ├─ starts game in that Job Object, same console
                                   ├─ opens named pipe \\.\pipe\gamewarden.runner.<instanceId>
                                   └─ writes output to logs\<date>.log + ring buffer
Core ◄──named pipe──► Runner     commands: status · stdin <text> · ctrl-c · kill · tail <n>
                                 events:   output line · exited(code)
```

- Runners are **not** children of the Core service's job, so stopping the service does not stop games.
- If the Runner dies, the Job Object kills the game too: no orphaned game processes.
- On Core start, the Supervisor **reattaches** to every Runner pipe it finds for known instances (PID + process start time are stored to avoid PID reuse mistakes).
- Runner binaries are installed per version (`runner\<version>\`) so an upgrade can install a new Runner while old ones are still running. A Runner is replaced on the next restart of its instance.

### 4.3 Graceful stop

Adapters declare a stop method; Core implements them all:

| Method | Mechanism |
|---|---|
| `ctrl-c` | Runner ignores Ctrl+C itself and raises `CTRL_C_EVENT` on its own console; the game (same console) receives it. **[Unverified — M0 spike]** |
| `stdin` | Runner writes a command (e.g. `exit`) to the game's stdin. |
| `rcon` | Core connects with RCON and sends the command. |
| `rest` | Core calls the game's admin REST API. |
| `kill` | Last resort only. |

Every stop has a timeout from the adapter; after it, Core escalates to `kill` and records it in the activity feed.

## 5. Desired state and reconciliation

- Each instance stores `desiredState` (`running` | `stopped`), changed only by user actions or schedules.
- A reconciliation loop (on Core start, on Runner exit, and every 30 s) compares desired vs actual and acts: start, stop, or restart with back-off.
- Crash restarts follow the restart policy in [03-domain-model §4](03-domain-model.md#4-crash-and-restart-policy); too many crashes put the instance in `Faulted` and stop retrying.
- On Windows shutdown, Core stops all instances gracefully but **keeps** their desired state, so they come back after boot.

## 6. Identity of processes

| Process | Runs as | Why |
|---|---|---|
| Core | `LocalSystem` | Needs admin rights for firewall rules, prerequisite installs, process management. |
| Runner + game | **Proposed:** dedicated non-admin local user `gamewarden-run` | Mods are code. Anyone allowed to manage mods can run code inside the game process; it must not run as SYSTEM. **Open question OQ-02**, to validate in M0. |

All games run in Session 0 (no desktop). This is fine for headless dedicated servers **[Unverified per game]**.

## 7. Persistence

| Store | Content |
|---|---|
| SQLite `db\gamewarden.db` | users, role bindings, invites, sessions, instances (metadata + desired state), jobs, backups index, schedules, audit, notification settings |
| `instances\<id>\instance.json` | Human-readable copy of the instance definition (adapter, ports, settings values). Core writes it; it allows rebuilding the DB index from disk. |
| `secrets\` | Secret values (server passwords, webhook URLs) encrypted with DPAPI (machine scope). |
| Files | Game files, configs, saves, logs, backups — see [08-operations](08-operations.md#1-filesystem-layout). |

## 8. CLI

- Talks to Core over the same REST API on `127.0.0.1`.
- Authenticates with a local token at `C:\ProgramData\GameWarden\cli.token`, readable only by Administrators and SYSTEM.
- Commands (initial): `status`, `start|stop|restart <id>`, `backup <id>`, `logs <id> [-f]`, `owner reset-password` (emergency, local admin only).

## 9. Solution layout (planned)

```
src/
  GameWarden.Core/          ASP.NET Core host, modules
  GameWarden.Runner/        Runner exe
  GameWarden.Cli/           CLI exe
  GameWarden.Contracts/     DTOs, pipe protocol, adapter schema models (shared)
  GameWarden.Web/           panel (framework per OQ-01)
adapters/                   built-in adapters (valheim, palworld, terraria, ...)
tests/
  GameWarden.Core.Tests/
  GameWarden.Adapters.Tests/  validates every adapter against the schema
build/                      installer script, publish profiles
```

## 10. Tech stack

| Area | Choice | Status |
|---|---|---|
| Runtime | .NET 10 (LTS), self-contained publish | Decided |
| Hosting | `Microsoft.Extensions.Hosting.WindowsServices` | Decided |
| API | ASP.NET Core minimal APIs, ProblemDetails | Decided |
| Realtime | SignalR (WebSocket transport) | Decided |
| DB | SQLite + EF Core migrations | Decided |
| Logging | Structured logs to rolling files (e.g. Serilog) | Proposed |
| Panel framework | Blazor WebAssembly + component library **or** React + TypeScript + Vite | **Open (OQ-01)** |
| Hooks | Windows PowerShell 5.1 (preinstalled) | Decided |
| Installer | Inno Setup **or** WiX | Open (OQ-06) |

## 11. Development environment

Decision (2026-10-27): develop on a **Windows** machine, so Windows-specific parts (Runner, Ctrl+C, service, firewall, real game servers) can be run and debugged locally.

| Item | Choice |
|---|---|
| Dev machine | A Windows PC/VM **separate from the clean template VM** (keeps the template free of SDKs and dev tools) |
| Tools | .NET 10 SDK, Git for Windows, IDE (Visual Studio / Rider / VS Code + C# Dev Kit), Node.js LTS if OQ-01 = React |
| Running Core | `dotnet run` as a console app in dev; the same binary runs as a Windows Service in production (`UseWindowsService` detects the context) |
| Dev settings | `appsettings.Development.json`: data root e.g. `D:\GameWardenDev`, panel on `127.0.0.1` only, Runner/game as the current user (no `gamewarden-run`) |
| Admin rights | Only needed for firewall/service work; dev mode MAY use a no-op firewall so the IDE need not run elevated |
| Fast test loop | Adapter `fakegame` (a small console program: prints logs, answers stdin, saves on Ctrl+C, can crash on command) to test start/stop/crash/backup flows without SteamCMD downloads |
| Tests | Unit tests use fakes behind interfaces (`IProcessHost`, `IFirewall`, `ISecretStore`, `IPrerequisiteChecker`); Windows integration tests tagged `[Trait("Category","Windows")]` |
| Deploy to the game VM | Copy a self-contained publish (zip) and run `gamewarden install` until the installer exists (M8) |
