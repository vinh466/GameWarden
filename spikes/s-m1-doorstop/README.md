# Spike S-M1: BepInEx from a folder outside the Valheim server's own

- **Date:** 2026-10-30
- **Machine:** GitHub-hosted `windows-2025` runner, through [`.github/workflows/spike-mods-doorstop.yml`](../../.github/workflows/spike-mods-doorstop.yml) (runs on pushes to `spike/mods-doorstop` or by hand)
- **Question** ([13 §5](../../docs/13-mods.md#5-spikes)): does `valheim_server.exe` load BepInEx 5.4.2360 from `mods\sets\<id>\` through Unity Doorstop's launch arguments, as r2modman does, and stay vanilla when Doorstop is off?
- **Not covered:** the template VM, `gamewarden-run` as the user, the service context, a player joining

[`run.ps1`](run.ps1):

1. It installs Valheim's dedicated server with SteamCMD (app 896660, anonymous).
2. It puts the parts in place:
   - `BepInExPack_Valheim` 5.4.2360's `BepInEx\` goes into `mods\sets\1\`.
   - Its `winhttp.dll` goes into `server\`, with a `doorstop_config.ini` set to `enabled = false`.
   - Epic Loot 0.15.4, Jötunn 2.30.2 and JsonDotNET 13.0.4 go into `mods\sets\1\BepInEx\plugins\<Namespace-Name>\`.
3. It starts the server three times:
   - **on:** `--doorstop-enabled true --doorstop-target-assembly <set>\BepInEx\core\BepInEx.Preloader.dll`
   - **off:** `--doorstop-enabled false`
   - **none:** no Doorstop arguments

It passes when:

- in *on*, the set's `BepInEx\LogOutput.log` shows `Chainloader startup complete` and Epic Loot loading, and no log appears under `server\BepInEx\`;
- in *off* and *none*, BepInEx writes no log.

## Result

Filled in after the run.
