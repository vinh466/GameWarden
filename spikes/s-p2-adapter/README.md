# Spike S-P2: the Palworld adapter's values against the real server

- **Date:** 2026-10-31
- **Machine:** GitHub-hosted `windows-2025` runner, through [`.github/workflows/spike-palworld-adapter.yml`](../../.github/workflows/spike-palworld-adapter.yml) (runs on pushes to `spike/palworld-adapter` or by hand).
- **Question** ([14 §5](../../docs/14-palworld.md#5-spikes)): does the dedicated server accept the values the adapter offers, and does it parse the quoted `DenyTechnologyList` form?

[`run.ps1`](run.ps1) installs app 2394010 once, then starts the server with three rendered `PalWorldSettings.ini` variants and reads `GET /v1/api/settings` back:

| Variant | Values |
|---|---|
| `baseline` | `Difficulty=None`, `DeathPenalty=Item`, `RandomizerType=None`, `CrossplayPlatforms=(Steam)`, `ServerPlayerMaxNum=16` |
| `hard-pvp-all` | `Difficulty=Hard`, `DeathPenalty=All`, `RandomizerType=Region`, `DenyTechnologyList=("RepairBench")`, `bIsPvP=True`, `ExpRate=2.000000` |
| `normal-itemequipment` | `Difficulty=Normal`, `DeathPenalty=ItemAndEquipment`, `RandomizerType=All`, `DenyTechnologyList=("RepairBench")`, `ExpRate=3.000000`, `ServerPlayerMaxNum=32` |

It records what the server answers, the rewritten config, the exit code and the logs in `out/results.json` and the per-variant files.

## Result

Pending the run. The values under test are the ones that are not verified by the official docs: `Difficulty` (the community wiki says the dedicated server does not use it), the `DeathPenalty`/`RandomizerType` options, and the quoted technology list.

## Not covered

The VM, `gamewarden-run` as the user, the service, a friend joining, and the memory over a few hours (S-P2's second half, [10 §8.6](../../docs/10-build-test-release.md#86-walk-through-for-palworld-l2)).
