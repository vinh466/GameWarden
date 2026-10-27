# 08 · Operations

## 1. Filesystem layout

Binaries and data are separate. Game files are **disposable**; config, saves and backups are **precious**.

```
C:\Program Files\GameWarden\          binaries (replaced on upgrade)
  core\                               GameWarden.Core + web panel static files
  runner\<version>\                   Runner per version (old ones kept while in use)
  cli\gamewarden.exe
  adapters-builtin\                   built-in adapters shipped with this version

C:\ProgramData\GameWarden\
  cli.token                           loopback CLI token (Administrators + SYSTEM only)

C:\GameWarden\                        data root (configurable)
  adapters\                           active adapters (built-in copied here + Owner's own)
  instances\
    <instance-id>\
      instance.json                   instance definition (written by Core)
      server\                         game files from SteamCMD/zip — disposable
      config\                         rendered config copies (for diff/inspection)
      data\                           saves / worlds — precious
      mods\                           mods.json + cache
      logs\                           console logs, one file per day
  backups\<instance-id>\              <yyyyMMdd-HHmmss>-<reason>.zip
  archive\<instance-id>\              archived instances (data + config, no game files)
  tools\steamcmd\
  db\gamewarden.db
  secrets\                            DPAPI-encrypted values
  logs\core\                          Core logs (rolling)
  logs\jobs\                          one log file per job
```

Short root path on purpose: mod folders can be deep, and short paths avoid Windows `MAX_PATH` issues.

Saves: adapters SHOULD point the game at `data\` with a launch argument; otherwise `savesRedirect` creates a junction from the game's save folder to `data\`.

## 2. Prerequisites

| Id | Check | Install source |
|---|---|---|
| `vcredist-x64`, `vcredist-x86` | Uninstall registry entries for VC++ 2015–2022 | Official Microsoft redistributable **[Unverified: exact URLs]** |
| `directx-legacy` | `d3dx9_43.dll`, `xinput1_3.dll` in `System32` | DirectX End-User Runtime (June 2010) **[Unverified: exact URL]** |
| `steamcmd` | `tools\steamcmd\steamcmd.exe` exists and runs | Official SteamCMD zip from Valve |
| `disk` | Free space ≥ adapter `resources.diskMb` + 20 % | – |
| `ports` | Requested ports not used by other instances or listening processes | – |

Checked at setup, on the System page, and before every start (only the adapter's declared prerequisites). Downloads are verified (size + signature/hash where available) before running.

Measured 2026-10-27: the template VM was missing VC++ and DirectX legacy runtime — the likely cause of the failed WindowsGSM starts **[Unverified]**.

## 3. Install, upgrade, uninstall

| Operation | Behavior |
|---|---|
| Install | See [07 §3](07-ux-and-user-flows.md#3-install). Creates the `gamewarden-run` user if OQ-02 is accepted. |
| Upgrade | Installer stops `GameWardenCore` (games keep running in their Runners), replaces `core\`, adds `runner\<new>\`, runs DB migrations on start, Core reattaches to Runners. Instances switch to the new Runner on their next restart. |
| Rollback | Re-run the previous installer; DB migrations MUST be backward-compatible within a minor version. A DB backup is taken before migrations. |
| Uninstall | Stops all instances gracefully, removes service and firewall rules. Data root is kept unless "Remove all data" is ticked (with a warning). |

## 4. Backups

| Topic | Rule |
|---|---|
| Content | Adapter `backup.paths` (usually `data\`) + `config\` + `instance.json` + `mods\mods.json` |
| Format | ZIP, `<yyyyMMdd-HHmmss>-<reason>.zip` |
| Consistency | Adapter `backup.mode`: `stop` (stop → copy → start), `saveThenCopy` (send `saveCommand`, wait, copy), `hot` (copy while running) |
| Triggers | Schedule (default daily 04:00), manual, and automatic before update / restore / delete |
| Retention | Keep last N scheduled (default 14); keep pre-operation backups 30 days; pinned backups never pruned |
| Restore | Stop → pre-restore backup → clear `data\` → extract → start if desired running |
| Off-VM copy | Not in v1. Backups live on the same disk as the VM → see OQ-08. |

## 5. Logging

| Log | Location | Retention |
|---|---|---|
| Core | `logs\core\core-<date>.log` (structured) | 30 days |
| Jobs | `logs\jobs\<jobId>.log` | 90 days |
| Game console | `instances\<id>\logs\<date>.log` | 14 days |
| Windows Event Log | Application log, source `GameWarden` (start, stop, failure only) | Windows default |

All logs pass through secret redaction.

## 6. Configuration

- `C:\Program Files\GameWarden\core\appsettings.json` — shipped defaults (do not edit).
- `C:\GameWarden\gamewarden.json` — Owner overrides set from the System page (bind addresses, port, VPN CIDRs, backup defaults, data paths).
- Environment variables `GAMEWARDEN__*` override both (for development).

## 7. Firewall

- One inbound rule per instance port: `GameWarden:<instanceId>:<port>/<proto>`, remote addresses = configured VPN CIDRs.
- Panel rule: `GameWarden:panel:<port>/tcp`, same scope.
- Core reconciles rules on start (adds missing, removes orphans with the `GameWarden:` prefix).

## 8. Disaster recovery

1. Re-create the VM from the template (or a new VM), install GameWarden.
2. Stop Core, copy back `C:\GameWarden\` (at least `db\`, `instances\*\data`, `instances\*\config`, `instances\*\instance.json`, `backups\`, `secrets\`).
3. Start Core → reinstall game files with **Verify / Install** per instance.

Note: DPAPI machine-scope secrets cannot be decrypted on a different machine. After moving to a new VM the Owner must re-enter secrets (server passwords, webhook). The panel lists secrets that could not be decrypted.
