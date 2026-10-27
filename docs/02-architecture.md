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

Full picture: [01-system-context](diagrams/01-system-context.excalidraw). All diagrams: [diagrams/](diagrams/).

## 2. Components

| Component | Kind | Responsibility |
|---|---|---|
| **Core** (`GameWarden.Core`) | Windows Service, ASP.NET Core host | All state and decisions: instances, jobs, scheduling, auth, API, panel hosting. |
| **Runner** (`GameWarden.Runner`) | Small console exe, one per running instance | Starts the game process, owns its console (stdin/stdout, Ctrl+C), streams output, survives Core restarts. |
| **CLI** (`gamewarden`) | Console exe | Status and emergency operations over the loopback API (§8). |
| **Web panel** (`GameWarden.Web`) | React + TypeScript SPA built with Vite ([ADR-0010](../decisions/0010-panel-react-typescript.md)); static files served by Core | All user interaction. |
| **Adapters** | Data + scripts on disk | Per-game knowledge. See [04-adapter-spec](04-adapter-spec.md). |
| **SteamCMD** | External tool | Downloads/updates game server files. Core runs **one SteamCMD at a time**. |

## 3. Core modules

| Module | Responsibility |
|---|---|
| **Api** | REST endpoints, auth middleware, antiforgery, ProblemDetails errors. |
| **Realtime** | SignalR hub: pushes events (status, job progress, console lines) to authorized clients. |
| **Identity** | Users, password hashing, sessions, invites, role bindings, permission checks. |
| **Instances** | CRUD of instances, desired state, port allocation, settings rendering, resource checks (min / recommended). |
| **Supervisor** | Talks to Runners; start/stop/kill; readiness and crash detection; restart policy; reconciliation loop. |
| **Jobs** | Queue and execute long operations; progress + log; cancellation; per-instance locks; global SteamCMD lock. |
| **Scheduler** | Cron-like triggers: backups, restarts, update checks. Creates jobs. |
| **Adapters** | Load, validate and version adapters; expose schemas to the panel; run hooks. |
| **Steam** | SteamCMD bootstrap, install/update, build-id check, output parsing for progress. |
| **Backups** | Create/restore/prune archives; pre-operation safety backups. |
| **Prereqs** | Detect and install VC++ runtime, DirectX runtime, SteamCMD; disk/port checks. |
| **Firewall** | Create/remove Windows Firewall rules per instance port, scoped to VPN ranges. |
| **Notifications** | Event subscribers: Discord webhook (messages + one status message edited in place; see S6). |
| **Audit** | Append-only record of user actions. |
| **Storage** | SQLite (EF Core) + file layout under the data root. |

Modules communicate through an in-process **event bus**; notification, audit and realtime push are subscribers, so new outputs never change the modules that raise events.

Modules reach the OS only through interfaces (§11): `IProcessHost` (launch chain, [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)), `IRunnerClient` (pipe protocol), `IInstanceFiles` (file operations impersonated as `gamewarden-run`, [ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)), `ISecretStore`, `IFirewall`, `IPrerequisiteChecker`. Diagram: [02-core-modules](diagrams/02-core-modules.excalidraw).

## 4. Process model and the Runner

Diagrams: [03-process-and-identity](diagrams/03-process-and-identity.excalidraw), [06-flow-start-stop](diagrams/06-flow-start-stop.excalidraw), [07-flow-crash-and-reattach](diagrams/07-flow-crash-and-reattach.excalidraw).

### 4.1 Why a Runner

- If Core launched games directly, restarting or upgrading Core would either kill the games or orphan them with no way to send `save`/`exit` to their stdin.
- Each Runner gets **its own hidden console**, which makes Ctrl+C delivery possible (§4.3).

### 4.2 Lifecycle

```
Core ──LogonUser → CreateProcessAsUser──► Runner(instanceId, launchSpec.json)   as gamewarden-run (ADR-0008)
       CREATE_NO_WINDOW, lpDesktop = ""     ├─ resets the inherited CTRL+C flag, adds a handler that returns TRUE
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
- Binaries are installed per version (`versions\<version>\`, [ADR-0013](../decisions/0013-own-setup-and-update-engine.md)) so an upgrade can install a new Runner while old ones are still running. A Runner is replaced on the next restart of its instance; Core speaks the pipe protocol of its own and the previous Runner version (OQ-26).

### 4.3 Graceful stop

Adapters declare a stop method; Core implements them all:

| Method | Mechanism |
|---|---|
| `ctrl-c` | Runner calls `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)` on its own console; the game (same console) receives it, and the Runner's handler returns TRUE so the Runner survives. Verified 2026-10-27 on the dev machine ([spikes/s1-ctrlc](../spikes/s1-ctrlc/)); with Valheim on the VM **[Unverified — S1 part 2]**. |
| `stdin` | Runner writes a command (e.g. `exit`) to the game's stdin. |
| `rcon` | Core connects with RCON and sends the command. |
| `rest` | Core calls the game's admin REST API. |
| `kill` | Last resort only. |

Every stop has a timeout from the adapter; after it, Core escalates to `kill` and records it in the activity feed.

Console rules (from spike S1 part 1 and the Microsoft console documentation):

- Core MUST start the Runner with `CREATE_NO_WINDOW`, which gives it its own console without a window. `DETACHED_PROCESS` would leave it without a console.
- The Runner MUST call `SetConsoleCtrlHandler(NULL, FALSE)` before starting the game. The "ignore CTRL+C" flag is inherited from parent processes, and the spike found it already set.
- The Runner then registers a handler that returns TRUE. It MUST NOT call `SetConsoleCtrlHandler(NULL, TRUE)` before starting the game, because the game would inherit it.
- The game MUST start on the Runner's console: no `CREATE_NEW_CONSOLE`, and no `CREATE_NEW_PROCESS_GROUP` (CTRL+C is disabled in a new process group).

## 5. Desired state and reconciliation

- Each instance stores `desiredState` (`running` | `stopped`), changed only by user actions or schedules.
- A reconciliation loop (on Core start, on Runner exit, and every 30 s) compares desired vs actual and acts: start, stop, or restart with back-off.
- Crash restarts follow the restart policy in [03-domain-model §4](03-domain-model.md#4-crash-and-restart-policy); too many crashes put the instance in `Faulted` and stop retrying.
- `Faulted` is persisted. After a Core or VM restart a `Faulted` instance stays `Faulted` and is not started automatically (OQ-16).
- On Windows shutdown, Core stops all instances gracefully but **keeps** their desired state, so they come back after boot. Core registers for `SERVICE_CONTROL_PRESHUTDOWN` with an explicit timeout (default 120 s **[Unverified: enough for Valheim to save]**) and stops instances in parallel. This needs an own service host ([ADR-0014](../decisions/0014-service-host-with-preshutdown.md), Proposed): .NET's service support has no pre-shutdown, and Windows' default pre-shutdown timeout is only 10 s; the VM's Windows Update settings are covered in [08 §9](08-operations.md#9-shutdown-and-windows-update) (OQ-21).

## 6. Identity of processes

| Process | Runs as | Why |
|---|---|---|
| Core | `LocalSystem` | Firewall rules, prerequisite installers, service control, starting processes as another user. |
| Runner + game, SteamCMD, adapter hooks | Non-admin local user `gamewarden-run` ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)) | Mods and hooks are code; they must not run as SYSTEM. Game behaviour under this user is checked in spike S3. |

Which identity may read, write or execute which folder: [ADR-0009](../decisions/0009-data-root-acl-and-secrets.md) and [04-filesystem-and-acl](diagrams/04-filesystem-and-acl.excalidraw).

All games run in Session 0 (no desktop). This is fine for headless dedicated servers **[Unverified per game]**.

## 7. Persistence

| Store | Content |
|---|---|
| SQLite `db\gamewarden.db` | users, role bindings, invites, sessions, instances (metadata + desired state), jobs, backups index, schedules, audit, notification settings |
| `instances\<id>\instance.json` | Human-readable export of the instance definition (adapter, ports, settings values), written by Core after every change. SQLite is the source of truth; the export is used only to rebuild the DB by hand (OQ-17). |
| `secrets\` | Secret values (server passwords, webhook URLs, the `gamewarden-run` password) encrypted with DPAPI in the `CurrentUser` scope of LocalSystem ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)). |
| `keys\` | ASP.NET Core Data Protection keys (session cookies, antiforgery), protected with DPAPI the same way. |
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
  GameWarden.Setup/         install / update / rollback / uninstall engine (ADR-0013)
  GameWarden.Contracts/     DTOs, pipe protocol, adapter schema models (shared)
  GameWarden.Web/           panel: React + TypeScript + Vite (ADR-0010)
adapters/                   built-in adapters (valheim, palworld, terraria, ...)
spikes/                     small experiments behind decisions (exists; not shipped)
tests/
  GameWarden.Core.Tests/
  GameWarden.Adapters.Tests/  validates every adapter against the schema
  FakeGame/                 test double for game servers (from spike S1)
build/                      publish profiles, release packaging, manifest signing tool
.github/workflows/          ci.yml, release.yml, smoke-real-games.yml (ADR-0012)
```

## 10. Tech stack

| Area | Choice | Status |
|---|---|---|
| Runtime | .NET 10 (LTS), self-contained publish | Decided |
| Hosting | .NET generic host; Windows service lifetime: own `IHostLifetime` with pre-shutdown instead of `Microsoft.Extensions.Hosting.WindowsServices` | Proposed ([ADR-0014](../decisions/0014-service-host-with-preshutdown.md)) |
| API | ASP.NET Core minimal APIs, ProblemDetails | Decided |
| Realtime | SignalR (WebSocket transport) | Decided |
| DB | SQLite + EF Core migrations | Decided |
| Logging | Structured logs to rolling files (e.g. Serilog) | Proposed |
| Panel framework | React + TypeScript + Vite; React Router, TanStack Query, `@microsoft/signalr`; component library (Mantine or shadcn/ui) chosen in M2 | Decided ([ADR-0010](../decisions/0010-panel-react-typescript.md)) |
| Panel API types | Generated from Core's OpenAPI document (`Microsoft.AspNetCore.OpenApi` → e.g. `openapi-typescript`) | Decided ([ADR-0010](../decisions/0010-panel-react-typescript.md)) |
| Win32 interop | P/Invoke for `LogonUser`, `CreateProcessAsUser`, Job Objects, console control (candidate: Microsoft.Windows.CsWin32 source generator) | Proposed |
| Hooks | Windows PowerShell 5.1 (preinstalled) | Decided |
| Installer and updates | Own Setup engine in C#; `Setup.exe` on GitHub Releases; side-by-side versions; signed `SHA256SUMS` | Proposed ([ADR-0013](../decisions/0013-own-setup-and-update-engine.md)) |
| CI | GitHub Actions on GitHub-hosted runners; test categories `Unit`, `Api`, `Windows`, `WindowsAdmin` | Proposed ([ADR-0012](../decisions/0012-ci-and-tests-on-github-hosted-runners.md)) |
| Tests | xUnit, `FakeTimeProvider`, `WebApplicationFactory`; Vitest; Playwright | Proposed ([10 §2](10-build-test-release.md#2-test-strategy)) |

## 11. Development environment

Decision (2026-10-27): develop on a **Windows** machine, so Windows-specific parts (Runner, Ctrl+C, service, firewall, real game servers) can be run and debugged locally.

| Item | Choice |
|---|---|
| Dev machine | The Owner's personal Windows 11 PC (confirmed 2026-10-27), **separate from the clean template VM**, which stays free of SDKs and dev tools |
| Tools | .NET 10 SDK, Git for Windows, IDE (Visual Studio / Rider / VS Code + C# Dev Kit), Node.js LTS (panel build) |
| Running Core | `dotnet run` as a console app in dev; the same binary runs as a Windows Service in production (`UseWindowsService` detects the context) |
| Dev settings | `appsettings.Development.json`: data root e.g. `D:\GameWardenDev`, panel on `127.0.0.1` only, Runner/game as the current user via plain `CreateProcess` (no `gamewarden-run`, no impersonation) |
| Admin rights | Only needed for firewall/service work; dev mode MAY use a no-op firewall so the IDE need not run elevated |
| Fast test loop | Adapter `fakegame` (a small console program: prints logs, answers stdin, saves on Ctrl+C, can crash on command) to test start/stop/crash/backup flows without SteamCMD downloads |
| Tests | Unit tests use fakes behind interfaces (`IProcessHost`, `IRunnerClient`, `IInstanceFiles`, `IFirewall`, `ISecretStore`, `IPrerequisiteChecker`); Windows integration tests tagged `[Trait("Category","Windows")]`; tests that change the machine (users, rights, ACLs, service, firewall) tagged `WindowsAdmin` and run only in CI ([10 §2](10-build-test-release.md#2-test-strategy)) |
| Deploy to the game VM | Download `GameWarden-Setup-<v>.exe` from GitHub Releases and run it; later versions update from the panel ([10 §5–6](10-build-test-release.md#5-first-install-on-the-vm)) |
