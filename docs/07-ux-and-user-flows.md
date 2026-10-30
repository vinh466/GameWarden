# 07 · UX and user flows

**MVP:** the Owner is the only panel user ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)). Screens and flows for friends (Users & Invites, invites, Share, the role-aware examples in §9) arrive in milestone M6; the rest applies to the MVP.

## 1. Principles

1. **Destructive actions have a safety net.** Update, restore and delete take a backup first. Purge requires typing the instance id.
2. **Long work is a job.** Progress, live log, cancel. Conflicting buttons are disabled with the reason shown ("Updating — job #42").
3. **No command line needed.** Everything is doable in the panel; L0 games show an on-screen guide.
4. **State is always visible.** Live status without refresh; connection loss is obvious.
5. **Role-aware.** Users only see what they can use; nothing teases a disabled admin feature to a Player.
6. **Phone-friendly.** Daily operations (status, restart, console tail, backup) work on a phone screen.

## 2. Lifecycle

```
Install ─► First-run setup ─► Create server ─► Invite friends / Share
                                    │
                                    ▼
             ┌──────── Daily operation ─────────┐
             │ watch · restart · settings ·     │
             │ update · mods · backup / restore │
             └──────────────────────────────────┘
                                    │
                                    ▼
                         Archive / purge server
```

## 3. Install

1. Owner downloads `GameWarden-Setup-<version>.exe` from the GitHub Releases page on the VM and runs it (UAC prompt; SmartScreen warning while binaries are unsigned). Details: [10 §5](10-build-test-release.md#5-first-install-on-the-vm).
2. Pre-checks: Windows version, admin rights, panel port 8765 free, free disk.
3. Installs binaries to `C:\Program Files\GameWarden\versions\<version>\`, data root `C:\GameWarden\` (changeable).
4. Registers service `GameWardenCore` (automatic, delayed start; recovery: restart on failure).
5. Creates Start Menu entries: **GameWarden Dashboard** (URL shortcut), **GameWarden Logs**, **Uninstall**; optional desktop shortcut.
6. Finish page: ☑ *Open dashboard* → browser opens `http://localhost:<port>/setup?token=<one-time setup token>`. `gamewarden setup-url` prints the same URL again.

## 4. First-run setup wizard (loopback only)

| Step | Content |
|---|---|
| 1. Owner account | Username + password (the setup token comes from the URL) |
| 2. Prerequisites | Table: VC++ x64/x86, DirectX legacy runtime, SteamCMD, disk space. Each missing item has **[Install]** (runs a job with progress). Can continue with warnings. |
| 3. Network | Detected interfaces (Radmin 26.x, Tailscale 100.x, LAN). Choose which networks may reach the panel (default: loopback + VPN). Choose VPN ranges allowed to reach game ports (default: the detected VPNs). |
| 4. Notifications (optional) | Discord webhook URL + **[Send test]** |
| 5. Defaults | Backup folder (default `C:\GameWarden\backups`), backup time (default 04:00, VM local time), backups to keep (default 14) |
| Done | Go to Dashboard |

Dashboard then shows a dismissible **Getting started** card:

```
Getting started
 ✓ Owner account created
 ✓ Prerequisites installed
 ☐ Create your first server      [Create]
 ☐ Connect Discord notifications [Open]
 ☐ Check backup schedule         [Open]
```

## 5. Navigation

| Item | Visible to |
|---|---|
| **Dashboard** | everyone |
| **Servers → {instance}** | users who can view it |
| **Activity** | everyone (filtered) |
| **Adapters** | everyone (read-only) / Owner (manage) |
| **Users & Invites** | Owner |
| **System** | Owner |
| Account menu: change password, logout | everyone |

## 6. Screens

### 6.1 Dashboard

```
┌ GameWarden ─────────────────────────── [+ New server]  an ▾ ┐
│ Core ● connected · up 3d 4h · v0.1.0                        │
│ VM  CPU ▓▓▓░░ 42%   RAM ▓▓▓▓░ 5.1/8 GB   Disk ▓▓░░░ 24/63 GB │
├─────────────────────────────────────────────────────────────┤
│ ┌ valheim-main ──────────┐  ┌ palworld-coop ─────────┐      │
│ │ Valheim      ● Running │  │ Palworld     ○ Stopped │      │
│ │ 3/10 players · 5h12m   │  │ Last backup: 04:00     │      │
│ │ CPU 18% · RAM 2.3 GB   │  │ Update available ⬆     │      │
│ │ [Stop][Restart][Share] │  │ [Start][Update]        │      │
│ └────────────────────────┘  └────────────────────────┘      │
├─────────────────────────────────────────────────────────────┤
│ Activity                                                    │
│ 04:00  valheim-main   Backup done (412 MB)                  │
│ 01:47  valheim-main   Crashed → restarted automatically     │
│ 21:04  valheim-main   An restarted the server               │
└─────────────────────────────────────────────────────────────┘
```

Card status colors: green Running · blue Starting/Stopping · red Crashed/Faulted · grey Stopped/NotInstalled. A yellow *Busy* badge is added while a job holds the instance lock (`activeJobId`, OQ-15). Buttons on a card depend on permissions.

### 6.2 Server detail

```
valheim-main  ● Running            [Stop] [Restart] [Share] [⋯]
────────────────────────────────────────────────────────────────
 Overview │ Console │ Settings │ Mods │ Backups │ Schedules │ Info
```

| Tab | Content | Min. permission |
|---|---|---|
| Overview | State, desired state, uptime, players, CPU/RAM, ports, game build, adapter level, recent jobs | `instance.view` |
| Console | Live output, search, pause scroll, command box (allow-list hint for Operators) | `instance.console.read` |
| Settings | Form from schema, grouped; "advanced" collapsed. Save → banner *Restart required* [Restart now] [Later] | `instance.settings.read` (edit: `.write`) |
| Mods | L3: list, add, remove, update, pin version. Lower levels: guide as checklist. The tab arrives in phase 2 of [13](13-mods.md#6-plan); until then mods and *Mods on* are set through the API (the CLI follows in phase 1c) | `instance.details.read` (edit: `instance.mods.manage`) |
| Backups | List with reason badges, [Backup now], [Restore], [Download], pin to keep | `instance.details.read` |
| Schedules | Backup / restart / update-check toggles and times (VM local time) | `instance.details.read` (edit: `.schedule.manage`) |
| Info | Paths, adapter, launch command (secrets redacted) | `instance.settings.read` |

### 6.3 New server wizard

```
① Game                 ② Name            ③ Settings          ④ Review
[Valheim     L3]       id: valheim-main   form with           ports 2456–2457/udp (auto)
[Palworld    L2]       name: Main world   defaults            CPU/RAM/disk: min ✓ recommended ⚠
[Terraria    L1]                                              prerequisites ✓
[Enshrouded  L0 guide]                                        [Create & install]
```

After **Create & install**: job progress view (SteamCMD %), then **[Start]**.

### 6.4 Other pages

- **Activity:** full feed with filters (instance, user, type); job entries open their log.
- **Adapters:** list with level badge, version, validation errors; Owner: [Reload].
- **Users & Invites:** users, bindings editor (role × scope), invites with expiry and [Revoke].
- **System:** prerequisites, network bindings, firewall ranges, notifications, Core version, data root, disk usage.

## 7. Key flows

Sequence diagrams: [06 start / stop](diagrams/06-flow-start-stop.excalidraw) · [07 crash and reattach](diagrams/07-flow-crash-and-reattach.excalidraw) · [08 update / backup / restore](diagrams/08-flow-update-backup-restore.excalidraw) · [09 access](diagrams/09-flow-access.excalidraw).

| Situation | Flow |
|---|---|
| **Invite a friend** | Users & Invites → [Invite] → role + scope + expiry → copy link → friend opens over VPN → sets username/password → lands on Dashboard seeing only allowed servers. |
| **Share connection info** | [Share] → ready-to-paste text: server name, `100.x.y.z:2456`, password (if allowed), required client mods → [Copy]. |
| **"Server is dead"** (friend at 1 a.m.) | Phone → Dashboard → red card → Console shows last lines → [Restart]. Activity records who restarted. |
| **Game update** | Badge *Update available* → [Update] → one job: announce in game (if supported) → stop → pre-update backup → SteamCMD → start. |
| **Change settings** | Settings → edit → Save → *Restart required* → [Restart now] or [Later]. |
| **Restore** | Backups → pick → [Restore] → confirm dialog shows date and that a safety backup will be taken → job: stop → pre-restore backup → extract to a temporary folder, then swap with `data\` → start (if desired running). |
| **Not enough resources** | [Start] below the adapter's minimum → blocked with what is missing (e.g. "needs 4 GB free RAM, 2.1 GB free") and **[Start anyway]** for the Owner; below the recommendation → starts with a warning. |
| **Delete** | ⋯ → Delete → **Archive** (keep data + config, remove game files) or **Purge** (type the id) → pre-delete backup in both cases. |
| **Crash loop** | After restart limit: card turns red *Faulted*, Discord alert, button [Start] resets the counter. |

## 8. "Is it running?" — visibility without a tray app

| Where you are | What you see | Milestone |
|---|---|---|
| Browser tab | Favicon color = what is happening now (green running / yellow busy / red Core unreachable / grey nothing runs); it clears itself. Problems that wait for someone (failed job, restart after a failure) are on the Dashboard only, since a tab cannot dismiss them: the `(1⚠)` title prefix was removed after the Owner's review (2026-10-28) | M2 |
| Dashboard header | `Core ● connected · up … · version`; red banner *Lost connection to Core — retrying…* when the realtime link drops | M2 |
| Discord | Event messages (up, down, crash, faulted, update available, backup failed) + one **status message** that is edited in place with the current state of all servers (webhooks can edit their own messages but cannot pin; pin it once by hand — S6) | Backlog (was M4) |
| On the VM | `gamewarden status` CLI; `services.msc` shows `GameWardenCore`; Windows Event Log entries for Core start/stop/failure | M1 |
| Monitoring (optional) | `GET /healthz` for an external checker (e.g. Uptime Kuma) to detect a dead Core | Backlog (was M4) |
| After a Core failure | Service recovery restarts Core; Dashboard shows *Core restarted after a failure at 02:13* | M2 |

Deferred / excluded: PWA and web push (need HTTPS, excluded by ADR-0005), public status page, logon toast.

## 9. Role-aware examples

| Element | Owner | Admin | Operator | Player |
|---|---|---|---|---|
| [+ New server] | ✓ | ✓ (global) | – | – |
| Card buttons | all | all | Start/Stop/Restart/Share | Share |
| Tabs | all | all | Overview, Console, Settings (read-only), Backups, Schedules (read-only), Mods (read-only) | Overview |
| System, Users | ✓ | – | – | – |
