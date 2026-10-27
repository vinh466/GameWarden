# 07 · UX and user flows

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

1. Owner runs `GameWarden-Setup.exe` as Administrator.
2. Pre-checks: Windows version, admin rights, panel port free, free disk.
3. Installs binaries to `C:\Program Files\GameWarden\`, data root `C:\GameWarden\` (changeable).
4. Registers service `GameWardenCore` (automatic, delayed start; recovery: restart on failure).
5. Creates Start Menu entries: **GameWarden Dashboard** (URL shortcut), **GameWarden Logs**, **Uninstall**; optional desktop shortcut.
6. Finish page: ☑ *Open dashboard* → browser opens `http://localhost:<port>/setup`.

## 4. First-run setup wizard (loopback only)

| Step | Content |
|---|---|
| 1. Owner account | Username + password |
| 2. Prerequisites | Table: VC++ x64/x86, DirectX legacy runtime, SteamCMD, disk space. Each missing item has **[Install]** (runs a job with progress). Can continue with warnings. |
| 3. Network | Detected interfaces (Tailscale 100.x, Radmin 26.x, LAN). Choose where the panel listens (default: loopback + VPN). Choose VPN ranges allowed to reach game ports. |
| 4. Notifications (optional) | Discord webhook URL + **[Send test]** |
| 5. Defaults | Backup time (default 04:00), backups to keep (default 14) |
| Done | Go to Dashboard |

Dashboard then shows a dismissible **Getting started** card:

```
Getting started
 ✓ Owner account created
 ✓ Prerequisites installed
 ☐ Create your first server      [Create]
 ☐ Invite a friend               [Invite]
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

Card status colors: green Running · blue Starting/Stopping · yellow Busy (job) · red Crashed/Faulted · grey Stopped/NotInstalled. Buttons on a card depend on permissions.

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
| Mods | L3: list, add, remove, update, pin version. Lower levels: guide as checklist | `instance.view` (edit: `instance.mods.manage`) |
| Backups | List with reason badges, [Backup now], [Restore], [Download], pin to keep | `instance.view` |
| Schedules | Backup / restart / update-check toggles and times | `instance.view` (edit: `.schedule.manage`) |
| Info | Paths, adapter, launch command (secrets redacted) | `instance.settings.read` |

### 6.3 New server wizard

```
① Game                 ② Name            ③ Settings          ④ Review
[Valheim     L3]       id: valheim-main   form with           ports 2456–2457/udp (auto)
[Palworld    L2]       name: Main world   defaults            RAM ~3 GB · disk ~2 GB ✓
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
| **Restore** | Backups → pick → [Restore] → confirm dialog shows date and that a safety backup will be taken → job: stop → pre-restore backup → restore → start (if desired running). |
| **Delete** | ⋯ → Delete → **Archive** (keep data + config, remove game files) or **Purge** (type the id) → pre-delete backup in both cases. |
| **Crash loop** | After restart limit: card turns red *Faulted*, Discord alert, button [Start] resets the counter. |

## 8. "Is it running?" — visibility without a tray app

| Where you are | What you see | Milestone |
|---|---|---|
| Browser tab | Favicon color = overall status (green / yellow / red / grey); title prefix `(1⚠) GameWarden` when something needs attention | M2 |
| Dashboard header | `Core ● connected · up … · version`; red banner *Lost connection to Core — retrying…* when the realtime link drops | M2 |
| Discord | Event messages (up, down, crash, faulted, update available, backup failed) + one **status message** that is edited in place with the current state of all servers (webhooks can edit their own messages but cannot pin; pin it once by hand — S6) | M6 |
| On the VM | `gamewarden status` CLI; `services.msc` shows `GameWardenCore`; Windows Event Log entries for Core start/stop/failure | M1 |
| Monitoring (optional) | `GET /healthz` for an external checker (e.g. Uptime Kuma) to detect a dead Core | M6 |
| After a Core failure | Service recovery restarts Core; Dashboard shows *Core restarted after a failure at 02:13* | M2 |

Deferred / excluded: PWA and web push (need HTTPS, excluded by ADR-0005), public status page, logon toast.

## 9. Role-aware examples

| Element | Owner | Admin | Operator | Player |
|---|---|---|---|---|
| [+ New server] | ✓ | ✓ (global) | – | – |
| Card buttons | all | all | Start/Stop/Restart/Share | Share |
| Tabs | all | all | Overview, Console, Settings (read-only), Backups, Schedules (read-only), Mods (read-only) | Overview |
| System, Users | ✓ | – | – | – |
