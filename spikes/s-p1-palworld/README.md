# Spike S-P1: Palworld's dedicated server as an adapter would run it

- **Date:** 2026-10-31
- **Machine:** GitHub-hosted `windows-2025` runner, through [`.github/workflows/spike-palworld.yml`](../../.github/workflows/spike-palworld.yml) (runs on pushes to `spike/palworld` or by hand). The install folder had a space in its path on purpose.
- **Question** ([14 §5](../../docs/14-palworld.md#5-spikes)): can GameWarden install, configure, start, check, save and stop Palworld's server the way the design assumes?
- **Not covered:** the template VM, `gamewarden-run` as the user, the service context, a player joining, Ctrl+C.

[`run.ps1`](run.ps1):

1. Installs app 2394010 with SteamCMD, anonymously.
2. Keeps `DefaultPalWorldSettings.ini`, saved here as [`DefaultPalWorldSettings-1.0.5.102999.ini`](DefaultPalWorldSettings-1.0.5.102999.ini).
3. Writes `Pal\Saved\Config\WindowsServer\PalWorldSettings.ini` from the template's `OptionSettings=(...)` line, with a few fields replaced.
4. Starts `Pal\Binaries\Win64\PalServer-Win64-Shipping-Cmd.exe -port=8211 -players=8 -logformat=text` directly.
5. Polls the REST API until it answers.
6. Reads the settings, players and metrics.
7. Tries a wrong password.
8. Saves and shuts down through REST.

## Result

**Passed** on 2026-10-31 ([run 41108841363](https://github.com/vinh466/GameWarden/actions/runs/41108841363)), server version `v1.0.5.102999`.

| Check | Result |
|---|---|
| Install | SteamCMD anonymously, in 2 minutes; 5.4 GB on disk (hosting guides say 12–15 GB) |
| REST `GET /v1/api/info`, basic auth `admin:<AdminPassword>` | Answered 7 s after the start. The fields are `version`, `servername`, `description` and `worldguid`. |
| The rendered file is read at start | Yes. `GET /settings` returned `ServerName` "GameWarden spike", `ExpRate` 2 and `ServerPlayerMaxNum` 8 from the rendered line. |
| Wrong password | 401 |
| `POST /v1/api/save` | OK in 0.1 s |
| `POST /v1/api/shutdown` `{"waittime": 1, "message": …}` | OK. The process exited 2.5 s later with code 0. |
| Saves | `Pal\Saved\SaveGames\…` created under the install folder |
| The file after the run | The server writes `PalWorldSettings.ini` again. All 122 fields keep the same values; only the bytes change (2 bytes, the encoding or the line end). |

- **The template** has 122 fields in one `OptionSettings=(...)` line, under `[/Script/Pal.PalGameWorldSettings]`. Value forms:
  - `True` / `False`
  - floats with six decimals (`1.000000`)
  - integers
  - bare enum words (`None`, `Item`, `Text`)
  - quoted strings (`"…"`)
  - parenthesised lists (`(Steam,Xbox,PS5,Mac)`)
  - an empty value (`DenyTechnologyList=`)
- **Ready line on stdout:** `Running Palworld dedicated server on :8211`, after `REST API started on port 8212`.
- **The console's first line** (`Game version is v1.0.5.102999`) comes as UTF-16 text, while the rest is ASCII. The Runner's relay must not garble the following lines **[Unverified: what the Runner shows today]**.
- **Still for the VM:**
  - the same as `gamewarden-run` under the service;
  - a friend joining over the VPN;
  - the memory use over a few hours.
