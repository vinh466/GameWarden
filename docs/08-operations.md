# 08 · Operations

## 1. Filesystem layout

Binaries and data are separate. Game files are **disposable**; config, saves and backups are **precious**.

```
C:\Program Files\GameWarden\          binaries, one folder per version (ADR-0013)
  versions\<version>\                 one flat folder per version, kept while a Runner uses it:
    GameWarden.Core.exe               Core (+ web panel static files from M2)
    GameWarden.Runner.exe             Runner of this version
    gamewarden.exe                    CLI
    GameWarden.Setup.exe              Setup engine: install, update, rollback, uninstall
    adapters-builtin\                 built-in adapters, loaded read-only from here (OQ-18)
    version.txt                       (all four apps share one self-contained .NET runtime in this folder)
  gamewarden.cmd                      runs the current version's CLI (on the machine PATH)

C:\ProgramData\GameWarden\
  cli.token                           loopback CLI token (Administrators + SYSTEM only)
  setup.token                         one-time token for creating the Owner; deleted once the Owner exists

C:\GameWarden\                        data root (configurable)
  adapters\                           Owner's own adapters; same id overrides a built-in one
  instances\
    <instance-id>\
      instance.json                   instance definition (written by Core)
      server\                         game files from SteamCMD/zip — disposable
      config\                         rendered config copies (for diff/inspection)
      data\                           saves / worlds — precious
      mods\                           mods.json + cache
      logs\                           console logs, one file per day
  backups\<instance-id>\              <yyyyMMdd-HHmmss>-<reason>.zip (backupRoot, configurable)
  archive\<instance-id>\              archived instances (data + config, no game files)
  tools\steamcmd\                     SteamCMD; runner\<run>\ holds a SteamCMD run's Runner state while it runs
  db\gamewarden.db
  secrets\                            DPAPI-encrypted values (CurrentUser scope of SYSTEM)
  keys\                               ASP.NET Core Data Protection keys (sessions, CSRF tokens), encrypted with DPAPI
  updates\<version>\                  downloaded package, SHA256SUMS(.sig), db-before.db snapshot
  updates\last-result.json            outcome of the last install, update or switch (written by Setup)
  logs\core\                          Core logs (rolling)
  logs\jobs\                          one log file per job
  logs\setup\                         one log file per Setup run (install, apply, uninstall)
  gamewarden.json                     Owner overrides (§6)

C:\Users\gamewarden-run\              profile of the run-as user (ADR-0008); some games write AppData here
```

Short root path on purpose: mod folders can be deep, and short paths avoid Windows `MAX_PATH` issues.

Access: the data root has inheritance disabled; `gamewarden-run` may modify only `instances\<id>\{server, config, data, mods, logs}` and `tools\steamcmd\`, and read `adapters\` ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md), diagram [04-filesystem-and-acl](diagrams/04-filesystem-and-acl.excalidraw)).

Saves: adapters SHOULD point the game at `data\` with a launch argument; otherwise `savesRedirect` creates a junction from the game's save folder to `data\`.

## 2. Prerequisites

| Id | Check | Install source |
|---|---|---|
| `vcredist-x64`, `vcredist-x86` | Uninstall registry entries for VC++ 2015–2022 | Official Microsoft redistributable **[Unverified: exact URLs]** |
| `directx-legacy` | `d3dx9_43.dll`, `xinput1_3.dll` in `System32` | DirectX End-User Runtime (June 2010) **[Unverified: exact URL]** |
| `steamcmd` | `tools\steamcmd\steamcmd.exe` exists and runs | Official SteamCMD zip from Valve: `media.steampowered.com` first, then the `steamcdn-a.akamaihd.net` address from Valve's wiki (`SteamCmdDownloadUrl` in `gamewarden.json` goes before both) |
| `network` | The hosts GameWarden downloads from (`media.steampowered.com`, `steamcdn-a.akamaihd.net`, `api.github.com`) resolve to real addresses, not `127.0.0.1` | The VM's DNS settings; see the note below |
| `disk` | Free space ≥ adapter `resources.diskMb` + 20 % | – |
| `ports` | Requested ports not used by other instances or listening processes | – |
| `resources` | Logical processors, free RAM and free disk vs the adapter's `resources.min` (block; the Owner may force) and `resources.recommended` (warn) — OQ-07 | – |

Checked at setup, on the System page, and before every start (only the adapter's declared prerequisites, plus `resources`). Downloads are verified (size + signature/hash where available) before running.

Measured 2026-10-27: the template VM was missing VC++ and DirectX legacy runtime — the likely cause of the failed WindowsGSM starts **[Unverified]**.

`gamewarden system` shows each prerequisite with the adapters that need it (a missing one that no adapter needs is only informative; Valheim needs `vcredist-x64` only) and the DNS check of the `network` row.

**DNS that blocks Steam (measured 2026-10-28, Vietnam).** The Owner's ISP / router DNS answered `steamcdn-a.akamaihd.net` with `127.0.0.1`, so the first SteamCMD download failed with *"No connection could be made because the target machine actively refused it (steamcdn-a.akamaihd.net:443)"*; public DNS (`1.1.1.1`) returned the real Akamai addresses. Fix used on the template VM: set the VM's DNS to a public resolver (`Set-DnsClientServerAddress -InterfaceAlias "<adapter>" -ServerAddresses 1.1.1.1,8.8.8.8`). Since then GameWarden also downloads SteamCMD from `media.steampowered.com` first, which was not blocked; SteamCMD's own updates and game downloads may still need the other Steam hosts, so a VM on such a network should use a public resolver anyway.

## 3. Install, upgrade, uninstall

| Operation | Behavior |
|---|---|
| Install | See [07 §3](07-ux-and-user-flows.md#3-install) and [10 §5](10-build-test-release.md#5-first-install-on-the-vm). Setup creates the data root with an explicit ACL ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)) and the service; Core, on its first start as the service, creates the local user `gamewarden-run` (random password stored as a secret, `SeBatchLogonRight`, interactive and RDP logon denied, hidden from the sign-in screen — [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)) and re-creates it whenever it is missing. |
| Upgrade | From the panel or `gamewarden update`: download from GitHub Releases, verify the signed `SHA256SUMS`, extract to `versions\<new>\`, snapshot the DB, stop `GameWardenCore` (games keep running in their Runners), switch the service to the new version, start, check health, roll back on failure ([10 §6](10-build-test-release.md#6-self-update-from-github-releases), [ADR-0013](../decisions/0013-own-setup-and-update-engine.md)). Instances switch to the new Runner on their next restart. Built-in adapters update with the binaries; the Owner's adapters are never touched. |
| Rollback | Automatic when the new version fails its health check: restores the DB snapshot taken at update time, so migrations may be forward-only. By hand: `gamewarden version use <previous>`; add `--restore-db` when the older version cannot read the newer database (changes made since that update are lost). |
| Uninstall | *Apps & features*, or `GameWarden.Setup.exe uninstall [--remove-data]`: Core stops all instances gracefully and removes the firewall rules and the `gamewarden-run` user; then Setup removes the service, the PATH entry and the program files. The data root is kept unless `--remove-data`. |

**First sign-in.** While no account exists, Core keeps a one-time setup token. On the machine itself, run `gamewarden setup-url` in an Administrator terminal and open the link it prints (`http://localhost:8765/setup?token=…`) in a browser there; it creates the Owner and signs them in. The link works only from that machine. A forgotten password: `gamewarden owner reset-password` (Administrator), which also signs the Owner out everywhere.

## 4. Backups

**Not built:** backups left the MVP on 2026-10-30 and wait in the backlog ([ADR-0018](../decisions/0018-backups-leave-the-mvp.md)). Until they come back, stop the server and copy `instances\<id>\data\` by hand; the games' own backups stay on. This section stays the target design.

| Topic | Rule |
|---|---|
| Location | One folder for all instances: `<backupRoot>\<instance-id>\`. `backupRoot` defaults to `C:\GameWarden\backups` and can point at another local disk ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)); Core gives it the same ACL as `backups\` in [ADR-0009](../decisions/0009-data-root-acl-and-secrets.md) (SYSTEM + Administrators only) |
| Content | Adapter `backup.paths` (usually `data\`) + `config\` + `instance.json` + `mods\mods.json` |
| Format | ZIP, `<yyyyMMdd-HHmmss>-<reason>.zip` |
| Consistency | Adapter `backup.mode`: `stop` (stop → copy → start), `saveThenCopy` (send `saveCommand`, wait, copy), `hot` (copy while running) |
| Triggers | Schedule (default daily 04:00), manual, and automatic before update / restore / delete |
| Retention | Keep last N scheduled (default 14); keep pre-operation backups 30 days; pinned backups never pruned |
| Verification | After writing, the CRC of every entry is checked and the archive's SHA-256 is stored with the backup; restore checks it again; a failure raises `backup.failed` (OQ-29) |
| Core state | Nightly `VACUUM INTO` of `gamewarden.db` plus a copy of `gamewarden.json` into `<backupRoot>\_core\` (keep 14), so the one backup folder holds everything except secrets (OQ-27) |
| Low disk | Below 5 GB or 10 % free (configurable) on the data or backup volume: prune old scheduled backups first, then block backups, installs and updates and alert on Discord (OQ-30) |
| Restore | Stop → pre-restore backup → extract into a temporary folder next to `data\` → swap the folders → start if desired running. The replaced `data\` is deleted only after the swap succeeded (OQ-20). |
| Off-VM copy | Out of scope ([ADR-0011](../decisions/0011-mvp-scope-and-direction.md)): every backup is in `backupRoot`; copying that folder elsewhere (NAS, cloud sync, another PC) is up to the operator. |

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
- `C:\GameWarden\gamewarden.json` — Owner overrides set from the System page (bind addresses, port, VPN CIDRs, `backupRoot` and backup defaults, data paths).
- Environment variables `GAMEWARDEN__*` override both (for development).
- Panel access, in `gamewarden.json` under `GameWarden`; set on the System page (*Network → Change*, applied at once) or by hand (then restart the service):
  - `PanelPort` (8765).
  - `PanelAccessNetworks`: local networks the panel answers on besides loopback; default: the VPN ranges. A LAN range such as `192.168.1.0/24` is the Owner's explicit choice ([06 §2](06-security-and-access.md#2-network-exposure)).
  - `PanelHostNames`: host names besides `localhost` and IP addresses, e.g. a Tailscale MagicDNS name; other names are refused (DNS rebinding).
  - `VpnRanges`: remote ranges of the game-port firewall rules.

## 7. Firewall

- One inbound rule per instance port: `GameWarden:<instanceId>:<port>/<proto>`, remote addresses = configured VPN CIDRs.
- Panel rule: `GameWarden:panel:<port>/tcp`, open to the panel access networks (default: the VPN ranges); set at every Core start.
- Core reconciles rules on start (adds missing, removes orphans with the `GameWarden:` prefix).

## 8. Disaster recovery

1. Re-create the VM from the template (or a new VM), install GameWarden.
2. Stop Core, copy back `C:\GameWarden\` (at least `db\`, `instances\*\data`, `instances\*\config`, `instances\*\instance.json`, `backups\`, `secrets\`). If only the backup folder survived: restore the DB from `<backupRoot>\_core\`, then restore each instance from its latest backup.
3. Start Core → reinstall game files with **Verify / Install** per instance.

Note: DPAPI secrets cannot be decrypted on a different machine. After moving to a new VM the Owner must re-enter secrets (server passwords, webhook); the installer creates a new `gamewarden-run` password, Core re-applies the data-root ACL on start, and existing sessions become invalid. The panel lists secrets that could not be decrypted.

## 9. Shutdown and Windows Update

- Core registers for `SERVICE_CONTROL_PRESHUTDOWN` with an explicit timeout (default 120 s **[Unverified: enough for Valheim to save]**) and stops all instances in parallel through their normal stop method; desired state is kept, so they come back after boot (OQ-21). Windows' own default pre-shutdown timeout is 10 s, and .NET's service support does not handle pre-shutdown, hence the own service host ([ADR-0014](../decisions/0014-service-host-with-preshutdown.md)).
- The hypervisor that runs the VM MUST wait longer than the pre-shutdown timeout before it forces the VM off (spike S8 records the value).
- On the game VM, set Windows Update **active hours** to the usual play time and avoid automatic restarts during them (Settings → Windows Update → Advanced options, or the matching Group Policy). An unplanned reboot still saves the worlds through pre-shutdown, but kicks players.
- After a reboot the service starts automatically (delayed start) and reconciliation restarts every instance whose desired state is *running*; `Faulted` instances stay stopped.
