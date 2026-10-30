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

**Passed** on 2026-10-30 ([run 41059211816](https://github.com/vinh466/GameWarden/actions/runs/41059211816)):

| Case | BepInEx log in the set | Log in `server\BepInEx\` | Chainloader | Epic Loot loaded | Server ran |
|---|---|---|---|---|---|
| on | yes | no | yes | yes | yes |
| off | no | no | no | no | yes |
| none | no | no | no | no | yes |

- **on:** the set's `LogOutput.log` says:
  - `Running under Unity v6000.0.75`
  - `3 plugins to load`
  - `Loading [Jotunn 2.30.2]`, `Loading [Newtonsoft.Json Detector 1.0.0]` and `Loading [Epic Loot 0.15.4]`
  - `Chainloader startup complete`

  BepInEx used the set's `BepInEx\` folder as its root, and the game folder got no `BepInEx\` folder.
- **off and none:** the server started and wrote its game log, and BepInEx did not load.
- **SHA-256 of the downloads:**
  - `denikson-BepInExPack_Valheim-5.4.2360`: `6BCA77B1016ACFE0FFAEBB6534C84B12AE8D12E5A96ABBF59EC6E4526204FC6F`
  - `RandyKnapp-EpicLoot-0.15.4`: `D9D6BD59A77C9167A89AB17826462550DFD1936D6799B90D95C9022461E067F4`
  - `ValheimModding-Jotunn-2.30.2`: `8AAE92DA2BE0EB6820CD4CF57E2F6C1D6AD0D738D4915966E7C3D7A94E9A9B0F`
  - `ValheimModding-JsonDotNET-13.0.4`: `A221C7BE7163AB9739274B962FBD1F63ABDE65B1418839A62F02ACA210868147`
- **Two `AsyncResourceUpload failed` Unity errors** appeared in the log. They are expected on a machine without a GPU **[Unverified]**.
- **Still open for the VM:** the same as `gamewarden-run` under the service, and a player joining.
