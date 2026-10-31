# 14 · Palworld: a second game on the shared adapter model

**Status: Accepted (the Owner approved it on 2026-10-31); started the same day. Phase 1 (the Core model) and phase 2 (the adapter) are built; phase 3 (the VM walk-through) follows (§8).** Palworld comes after Valheim's mods, to harden the adapter model for more games ([ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)). Its decision is [ADR-0022](../decisions/0022-config-files-rendered-by-core-servers-controlled-over-http.md). Adapter spec: [04](04-adapter-spec.md).

## 1. Goal

The group plays Palworld on the same VM as Valheim, managed the same way: install, start, stop, restart after a crash, update, and settings in the panel. Palworld differs from Valheim where the adapter model is still thin:

- Its settings live in a file, not in launch arguments.
- It is stopped through an HTTP API, because nothing documents that Ctrl+C saves.
- Its readiness is best checked over HTTP.

Building it makes these parts of the model real, for the games after it.

## 2. The Owner's answers (2026-10-31)

| # | Question | Answer |
|---|---|---|
| 1 | Mods for Palworld? | No: the group plays vanilla. Palworld mods (UE4SS, `.pak`) can come later |
| 2 | Settings in the panel? | A form for **every** setting (all 122 fields), no raw editing |
| 3 | Resources? | The VM has enough RAM: Palworld runs next to Valheim |
| 4 | Platforms? | Steam only: `CrossplayPlatforms` = `(Steam)` by default |

## 3. Facts (checked 2026-10-31)

Sources: Pocketpair's server docs at [docs.palworldgame.com](https://docs.palworldgame.com/) (v1.0.4), and spike S-P1 on a `windows-2025` runner with server `v1.0.5.102999` ([spike](../spikes/s-p1-palworld/README.md)).

- **Version.** Palworld 1.0 came out on 2026-07-10 [Unverified: date from server hosts' posts], so it has left early access.
- **Install.** SteamCMD app `2394010`, anonymous: 2 minutes and 5.4 GB on the runner.
  - `PalServer.exe` is a launcher.
  - The server itself is `Pal\Binaries\Win64\PalServer-Win64-Shipping-Cmd.exe`, started directly so its console and its exit reach the Runner.
- **Arguments** ([docs](https://docs.palworldgame.com/settings-and-operation/arguments)): `-port=8211`, `-players=N`, `-publiclobby`, `-publicip`, `-publicport` (advertised only), `-logformat=text|json`.
  - The multithreading arguments are discouraged since 1.0 ("leaving this parameter unset may improve performance").
- **Settings.**
  - Where they live: `Pal\Saved\Config\WindowsServer\PalWorldSettings.ini`, section `[/Script/Pal.PalGameWorldSettings]`, one line `OptionSettings=(...)` with 122 fields.
  - Value forms: `True`/`False`; floats with six decimals; integers; bare enum words; quoted strings; parenthesised lists; empty values.
  - The template `DefaultPalWorldSettings.ini` sits in the install folder. Editing it changes nothing. The live folder exists only after a first start, unless something creates it.
  - The server reads the file at start (S-P1: the rendered `ServerName`, `ExpRate` and `ServerPlayerMaxNum` were in effect) and writes it back with the same values; the second run of S-P1 (2026-10-31) showed the only change is a trailing newline (2 bytes).
- **REST API** ([docs](https://docs.palworldgame.com/api/rest-api/palwold-rest-api)):
  - It is on when `RESTAPIEnabled=True`, on `RESTAPIPort` (8212), with basic auth `admin:<AdminPassword>`.
  - Endpoints: `info`, `players`, `settings`, `metrics`, `announce`, `kick`, `ban`, `unban`, `save`, `shutdown`, `stop` (force), `game-data`.
  - S-P1: `GET /v1/api/info` answered 7 s after the start, and a wrong password got 401. `POST /save` took 0.1 s. `POST /shutdown {"waittime": 1, …}` ended the process 2.5 s later with code 0.
  - The docs say the API is "not designed for direct Internet exposure".
- **Console.** It prints `REST API started on port 8212`, then `Running Palworld dedicated server on :8211`. The first line, the game version, is UTF-16 text.
- **Saves** go to `Pal\Saved\SaveGames\0\<world id>\` under the install folder. No argument moves them. With `bIsUseBackupSaveData` (on by default) the game keeps its own rolling backups ([docs](https://docs.palworldgame.com/settings-and-operation/configuration)).
- **Requirements** ([docs](https://docs.palworldgame.com/getting-started/requirements)): 4+ cores, 16 GB RAM (8 GB boots but risks running out of memory), an SSD, UDP 8211.

## 4. Design

### 4.1 The adapter

`adapters/palworld/`, version 0.1.0, L2.

| Part | Value |
|---|---|
| `source` | `steamcmd`, app `2394010` |
| `launch` | `{paths.server}\Pal\Binaries\Win64\PalServer-Win64-Shipping-Cmd.exe` with `-port={ports.game}`, `-players={settings.ServerPlayerMaxNum}`, `-logformat=text` |
| `ports` | `game` UDP 8211 (firewall rule for the VPN ranges); `rest` TCP 8212, **internal**: allocated, no firewall rule, used by Core on `127.0.0.1` |
| `files` | `settings`: `PalWorldSettings.ini` from the template `DefaultPalWorldSettings.ini`, format `ue-struct-ini`. Fixed fields: `RESTAPIEnabled=True`, `RESTAPIPort={ports.rest}`, `AdminPassword="{secrets.AdminPassword}"`, `ServerPassword="{secrets.ServerPassword}"`, `PublicPort={ports.game}`, `RCONEnabled=False` |
| `readiness` | `http`: `GET http://127.0.0.1:{ports.rest}/v1/api/info`, basic auth `admin` / `{secrets.AdminPassword}`, status 200, timeout 600 s |
| `stop` | `http`: `POST /v1/api/save`, then `POST /v1/api/shutdown` `{"waittime": 1, "message": "The server is stopping."}`; then wait for the exit, `timeoutSec` 120, then kill |
| secrets | `AdminPassword` (generated at create; the Owner may set their own, for example to type `/AdminPassword` in game), `ServerPassword` (optional; empty = no password) |
| `resources` | min 2 cores, 8 GB RAM, 15 GB disk; recommended 4 cores, 16 GB RAM, 25 GB disk |
| `backup.paths` | `{paths.server}\Pal\Saved\SaveGames` (the saves stay in the install folder; backups are in the backlog, ADR-0018) |
| `prerequisites` | `vcredist-x64` [Unverified: the official docs list none; the usual Unreal Engine 5 need] |

### 4.2 Settings: every field in the form

- **One setting per field of the template**, keyed by the field name. 116 settings, plus the two secrets; `RESTAPIEnabled`, `RESTAPIPort`, `PublicPort` and `RCONEnabled` are fixed.
- **Types follow the template's values:** `bool`; `number` (the six-decimal floats); `int`; `enum` where the values are known (`Difficulty`, `DeathPenalty`, `RandomizerType`, `LogFormatType`); `string`; `list` (`CrossplayPlatforms`: Steam, Xbox, PS5, Mac; `DenyTechnologyList`: free-text tags, written in quotes as the game's example shows).
- **Defaults are the template's**, except `CrossplayPlatforms` = `(Steam)` (§2, answer 4).
- **Groups:**
  - Server and joining
  - World and difficulty
  - Rates
  - Players
  - Pals
  - Death and PvP
  - Bases and buildings
  - Items and drops
  - Guilds
  - Voice chat
  - Saving and performance
  - Advanced (closed)
- **Labels** are in English and Vietnamese. A one-line description comes from Pocketpair's configuration page where it has one.
- **Help topics** cover the settings that need more than a line: difficulty and rates, death penalty, PvP and hardcore, base limits, auto save, crossplay, the admin password.
- **The form is long**, so the Settings tab gets a filter box that matches labels and keys, for every game.

### 4.3 Rendering

ADR-0022 decision 1. Before every start, Core renders `PalWorldSettings.ini`:

1. It reads the template's `OptionSettings` fields, in order.
2. It replaces the fixed fields, then every setting's field with its effective value.
3. It writes the section and the line as `gamewarden-run`, creating `Pal\Saved\Config\WindowsServer\` if needed.
4. It keeps a copy in `{paths.config}`.

How each value is written:
- strings quoted; they may not contain `"` or line breaks, which the settings check refuses;
- booleans `True`/`False`;
- numbers with six decimals, in the invariant culture;
- enums bare;
- lists `(A,B)`; a quoted list `("A","B")`, for `DenyTechnologyList`;
- an empty string as `""`, except where the template has an empty value.

The renderer is a pure function with unit tests: a round trip of the S-P1 template, and the seven value forms.

### 4.4 Start, readiness, stop

- **Start.** The Runner starts the Shipping-Cmd exe directly. The console is relayed as for Valheim. The UTF-16 first line must not break the relay of the lines after it.
- **Readiness.** Core polls the `http` check every 2 s until it answers 200 or 600 s pass. A wrong password (401) fails the start at once, naming the cause.
- **Stop.** Core sends the requests in order, each with its own short timeout, then waits for the process to exit. If a request fails (the API is not up yet, or the server hangs), Core goes on to wait and then kill, as other stop methods do. Windows shutdown uses the same path within the pre-shutdown budget (ADR-0014).
- **Crash restarts and updates** work as for Valheim.

### 4.5 Security

- The REST port gets no firewall rule; only Core reaches it, on loopback.
- `AdminPassword` is generated, stored as a secret (ADR-0009), and written into the game's INI as plain text, which is the game's design (OQ-14).
- `ServerPassword` is a secret like Valheim's password.

### 4.6 Not now

- Palworld mods (§2, answer 1).
- Player counts through `GET /players` (with the backlog's player probes, ADR-0019).
- RCON.
- `-publiclobby` (the server is for friends on the VPN).
- Moving the saves out of the install folder.

## 5. Spikes

| Id | Where | Question | Result |
|---|---|---|---|
| S-P1 | A `windows-2025` runner | Install, template, rendered settings read at start, REST readiness, save, shutdown | **Passed 2026-10-31** ([spike](../spikes/s-p1-palworld/README.md)) |
| S-P2 | The VM, first release candidate | The same as `gamewarden-run` under the service; a friend joins over the VPN; memory over a few hours | Phase 3 |

## 6. Plan

| Phase | Content | Checks |
|---|---|---|
| 0 | This document and ADR-0022; the Owner approves | Docs check; S-P1 |
| 1 | Core: file declarations and the `ue-struct-ini` renderer; the `number` and `list` types; `http` readiness and stop; internal ports; generated secrets; FakeGame gets a small HTTP mode for tests | Unit (renderer round trip against the S-P1 template), API and Integration on Linux; Windows CI |
| 2 | The `palworld` adapter (all settings, labels, help topics) — built 2026-10-31; screenshots and the release candidate next. The panel's `number` and `list` inputs and the Settings filter came with phase 1's pull request (2026-10-31) | Adapter validation, schema defaults against the S-P1 template, panel tests; release candidate |
| 3 | VM walk-through ([10 §8.6](10-build-test-release.md#86-walk-through-for-palworld-l2)): install, start, a setting in game, save and stop, a friend joins, crash restart, update | The Owner on the VM |

## 7. Questions

None open. On 2026-10-31 the Owner confirmed that GameWarden generates `AdminPassword` and that they may set their own in the Settings tab (for `/AdminPassword` in game), and approved this design.

## 8. Started on 2026-10-31

On 2026-10-31 the Owner put the build on hold to play Valheim; on the same day they started it. Steps:

1. [x] Check that the design still fits the current Palworld server. S-P1 ran again on 2026-10-31 ([run 41173047320](https://github.com/vinh466/GameWarden/actions/runs/41173047320)): server `v1.0.5.102999`, 122 fields with the same names and defaults (the new template differs only in line endings), REST ready after 8 s, wrong password 401, save then shutdown exited 0 after 2.4 s. The server appends a trailing newline (2 bytes) to the file it rewrites; the field values stay the same.
2. [x] Phase 1 (§6): the Core model of ADR-0022, with FakeGame's HTTP mode and no Palworld adapter yet: [FileRenderer](../src/GameWarden.Core/Adapters/FileRenderer.cs), [UeStructIni](../src/GameWarden.Core/Adapters/UeStructIni.cs), `http` readiness and stop, `number`/`list`, internal ports, generated secrets. Tests: the S-P1 template round trip (122 fields), the six value forms, the HTTP start/stop flow on Linux, the 401 failure path.
3. [x] Phase 2: the `palworld` adapter (0.1.0, L2), with all 116 settings, the two secrets, English and Vietnamese labels, and 8 help topics. The panel's inputs and filter are already in (§6). Screenshots and the release candidate are next.
4. [ ] Phase 3: the Owner's VM walk-through, [10 §8.6](10-build-test-release.md#86-walk-through-for-palworld-l2).
