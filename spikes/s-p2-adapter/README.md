# Spike S-P2: the Palworld adapter's values against the real server

- **Date:** 2026-10-31
- **Machine:** GitHub-hosted `windows-2025` runner, through [`.github/workflows/spike-palworld-adapter.yml`](../../.github/workflows/spike-palworld-adapter.yml) (runs on pushes to `spike/palworld-adapter` or by hand).
- **Question** ([14 §5](../../docs/14-palworld.md#5-spikes)): does the dedicated server accept the values the adapter offers, and does it parse the quoted `DenyTechnologyList` form?
- **Result:** **Passed** ([run 41179570904](https://github.com/vinh466/GameWarden/actions/runs/41179570904)). S-P1's run the same day reported server `v1.0.5.102999`; S-P2's template is identical to [S-P1's](../s-p1-palworld/DefaultPalWorldSettings-1.0.5.102999.ini) apart from line endings.

[`run.ps1`](run.ps1) installs app 2394010 once, then starts the server with three rendered `PalWorldSettings.ini` variants and reads `GET /v1/api/settings` back:

| Variant | Values | Ready | Exit | What the server answered |
|---|---|---|---|---|
| `baseline` | `Difficulty=None`, `DeathPenalty=Item`, `RandomizerType=None`, `CrossplayPlatforms=(Steam)`, `ServerPlayerMaxNum=16` | 7.0 s | 0 | `Difficulty=None`, `DeathPenalty=Item`, `RandomizerType=None`, `CrossplayPlatforms=["Steam"]`, `DenyTechnologyList=[]` |
| `hard-pvp-all` | `Difficulty=Hard`, `DeathPenalty=All`, `RandomizerType=Region`, `DenyTechnologyList=("RepairBench")`, `bIsPvP=True`, `ExpRate=2.000000` | 5.9 s | 0 | `Difficulty=Hard`, `DeathPenalty=All`, `RandomizerType=Region`, `DenyTechnologyList=["RepairBench"]`, `bIsPvP=true`, `ExpRate=2` |
| `normal-itemequipment` | `Difficulty=Normal`, `DeathPenalty=ItemAndEquipment`, `RandomizerType=All`, `DenyTechnologyList=("RepairBench")`, `ExpRate=3.000000`, `ServerPlayerMaxNum=32` | 22.0 s | 0 | `Difficulty=Normal`, `DeathPenalty=ItemAndEquipment`, `RandomizerType=All`, `DenyTechnologyList=["RepairBench"]`, `ExpRate=3`, `ServerPlayerMaxNum=16` (the launch argument, see below) |

- **Enums:** all the values the adapter offers are accepted and returned: `Difficulty` `None`/`Normal`/`Hard`, `DeathPenalty` `Item`/`ItemAndEquipment`/`All`, `RandomizerType` `None`/`Region`/`All`, `LogFormatType` `Text`. The community wiki says the dedicated server does not currently *use* `Difficulty` for world generation; the server still parses and returns the value. The adapter keeps the field, warns about it and leaves it out of the wizard.
- **Quoted list:** `DenyTechnologyList=("RepairBench")` was parsed as `["RepairBench"]` and written back unchanged after the run. The `quoted` list form is correct.
- **Crossplay:** `CrossplayPlatforms=(Steam)` was parsed as `["Steam"]`.
- **Fixed fields:** every variant's rewritten config kept `RESTAPIEnabled=True`, `RESTAPIPort=8212`, `PublicPort=8211`, `RCONEnabled=False`, a non-empty quoted `AdminPassword`, and `ServerPassword=""`.
- **`-players` wins over the INI:** the third variant wrote `ServerPlayerMaxNum=32` into the file but started with our launch argument `-players=16`; `/settings` returned 16 while the file kept 32. The adapter passes `{settings.ServerPlayerMaxNum}` in both places, so they cannot diverge there.
- **Console:** the first line is UTF-16 (garbled when read as UTF-8), then `REST API started on port 8212` and `Running Palworld dedicated server on :8211` are readable; the save + shutdown exit code was 0.

## Not covered

The VM, `gamewarden-run` as the user, the service, a friend joining, and the memory over a few hours (S-P2's second half, [10 §8.6](../../docs/10-build-test-release.md#86-walk-through-for-palworld-l2)).
