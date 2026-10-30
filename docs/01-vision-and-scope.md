# 01 · Vision and scope

## 1. Problem

Hosting Steam dedicated servers for friends (Valheim, Terraria, Palworld, ...) is currently done by hand: run SteamCMD commands, edit config files, start the server in a console window, copy save folders for backups. Every game is different, and nothing is recorded. Off-the-shelf managers were evaluated:

- **WindowsGSM** (tried 2026-10-27): servers failed to start. Its maintenance is sporadic: last release v1.23.1, then only a few merged PRs (latest 2025-12-26).
- **AMP** (paid, closed source) and **Pterodactyl/Pelican** (Linux + Docker only) do not fit "Windows + write my own per-game support".

See [ADR-0001](../decisions/0001-build-own-platform.md).

## 2. Vision

> A small, reliable "warden" that lives on the game VM, keeps the game servers in the state you asked for, and lets you and your friends operate them from a browser — adding support for a new game is writing an adapter, not changing the platform.

## 3. Goals

| ID | Goal |
|---|---|
| G1 | Install, update, start, stop and restart dedicated servers from a web panel, without the command line. |
| G2 | Keep servers in their **desired state**: auto-start after VM reboot, auto-restart after a crash (with limits). |
| G3 | Never lose a world: scheduled backups, automatic backup before risky operations (update, restore, delete), one-click restore. In the backlog since 2026-10-30 ([ADR-0018](../decisions/0018-backups-leave-the-mvp.md)). |
| G4 | Add a new game by writing an **adapter** (JSON + optional PowerShell hooks), with gradual support levels L0–L3. |
| G5 | Let friends log in with **roles and per-server scope** (Admin / Operator / Player); the Owner keeps system control. **After the MVP** ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)). |
| G6 | Make state obvious: live status, live console, activity feed with "who did what", Discord notifications. |
| G7 | Catch environment problems early: check prerequisites (VC++ runtime, DirectX, SteamCMD, disk space, ports) and the adapter's minimum / recommended CPU, RAM and disk before they cause a failed start. |
| G8 | Keep it light enough for a 4 vCPU / 8 GB VM: Core + panel should idle well under 200 MB RAM **[Unverified target]**. |

## 4. Non-goals (for now)

| Non-goal | Reason |
|---|---|
| Multiple hosts / clustering | One VM is enough for < 10 players per server. |
| Linux hosts, Docker | The chosen platform is Windows. The Linux plan in `gamehost-infra` (Pelican) is a technical reference only ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)). |
| Copying backups off the machine | GameWarden collects every backup in one folder (`backupRoot`); moving that folder elsewhere is up to the operator ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)). |
| Public internet exposure of the panel | Panel is reachable over VPN only ([ADR-0005](../decisions/0005-http-only-over-vpn.md)). |
| HTTPS, PWA, web push | Excluded by the HTTP-only decision. Can be revisited later. |
| Tray app | Web panel + other visibility channels cover the need ([ADR-0004](../decisions/0004-web-panel-only-no-tray.md)). |
| Multi-tenant hosting, billing, quotas per customer | Friends-only use. |
| In-browser file manager | Large attack surface; use RDP for rare manual file work. May be revisited. |
| Steam accounts with Steam Guard for SteamCMD | MVP supports anonymous SteamCMD logins only. Adapters declare if a game needs a login. |

## 5. Personas

In the MVP only the **Owner** uses the panel; friends only join the game servers. The friend personas apply from milestone M6 ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)).

| Persona | Who | Typical needs |
|---|---|---|
| **Owner** | The VM owner (exactly one person in practice, the model allows more) | Install GameWarden, manage system, adapters, users; everything an Admin can do. |
| **Friend-Admin** | A trusted friend | Create servers, change settings, manage mods, restore backups. |
| **Friend-Operator** | A friend who plays often | Restart a stuck server at 1 a.m., run `save`, take a backup. |
| **Player** | Any other friend | See whether the server is up and how to connect. |

## 6. Constraints and facts

| Fact | Source / date |
|---|---|
| Host VM: QEMU, Windows 10 IoT Enterprise LTSC 2021 (19044), Xeon E5-2696 v3, 4 vCPU, 7.9 GB RAM, 63 GB disk | Measured 2026-10-27 |
| The VM is a **template**; CPU/RAM/disk will be increased when needed | User, 2026-10-27 |
| Fresh VM has **no VC++ redistributables** and **no legacy DirectX runtime** (`d3dx9_43.dll`, `xinput1_3.dll` missing); .NET Framework 4.8 present, 3.5 absent | Measured 2026-10-27 |
| VPN: Tailscale installed on the VM (100.x address); friends mostly use **Radmin VPN** | Measured / user, 2026-10-27 |
| Usually one game server runs at a time; several should be possible, and the VM's CPU/RAM can be raised for them | User, 2026-10-27 |
| Dev machine: the Owner's personal Windows 11 PC; the game VM is a separate machine | User, 2026-10-27 |
| A Discord channel for notifications exists | User, 2026-10-27 |
| Default ACL of `C:\` gives `Authenticated Users` Modify on every new subfolder (`(OI)(CI)(IO)(M)`) | Measured on the Windows 11 dev machine, 2026-10-27; to re-check on the VM ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)) |
| < 10 players per server | User |
| Game files are installed with SteamCMD; mods come from common mod sites | User |
| Panel access is HTTP only | User decision, 2026-10-27 |

## 7. Assumptions

- **[Unverified]** Most target dedicated servers run fine as headless processes started from a Windows Service (Session 0). To be checked per game in M0.
- **[Unverified]** Valheim, Palworld and tModLoader servers are downloadable with an anonymous SteamCMD login; vanilla Terraria server comes as a zip from terraria.org.
- Friends join the games over the same VPN they already use (mostly Radmin); after the MVP, friends who use the panel reach it over that VPN too.

## 8. Success criteria

MVP (milestones M0–M2 since 2026-10-30, [ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)):

1. With GameWarden installed, the Owner gets from an empty server list to a running Valheim server that a friend can join, **using only the panel**.
2. Rebooting the VM brings every server whose desired state is *running* back up without anyone logging in.
3. Killing a game process results in an automatic restart and a Discord message. *The restart passed in M1; the Discord message waits with M4 in the backlog ([ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)).*
4. Restoring yesterday's backup works end-to-end and leaves a safety backup of the state it replaced. *Left the MVP with backups on 2026-10-30 ([ADR-0018](../decisions/0018-backups-leave-the-mvp.md)); it is the exit of that work when it comes back from the backlog.*
5. Starting an instance below its adapter's minimum resources is blocked with a message that says what is missing (the Owner can force it); below the recommended resources it starts with a warning.

After the MVP:

6. A friend with the Operator role can restart a server but cannot change its settings or see the Owner's system pages (M6).

## 9. Glossary

| Term | Meaning |
|---|---|
| **Core** | The GameWarden Windows Service. Owns all state and decisions. |
| **Runner** | Small helper process, one per running game server; owns the game process and its console. |
| **Adapter** | Folder describing how to install, configure, run and stop one game. |
| **Instance** | One concrete server created from an adapter (e.g. `valheim-main`). A game can have many instances. |
| **Desired state** | What the user asked for (`running` / `stopped`). Core keeps actual state converging to it. |
| **Job** | A long-running operation (install, update, backup, restore, mod sync) with progress and log. |
| **Support level** | L0 guide only · L1 install/run/stop/backup · L2 + settings form · L3 + automated mods. |
| **Provider** | Shared mod source integration (Thunderstore, Steam Workshop, ...). |
