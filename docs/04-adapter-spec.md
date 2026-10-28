# 04 · Adapter specification

An **adapter** teaches GameWarden one game. It is mostly declarative (JSON); code (PowerShell hooks) is only for what JSON cannot express. Core implements everything generic (process control, ports, firewall, backups, jobs, UI).

## 1. Support levels

| Level | Adapter provides | Panel shows |
|---|---|---|
| **L0** | `guide.md` only | Step-by-step guide; no automation |
| **L1** | install source, launch, stop, readiness, backup paths | Install / Start / Stop / Restart / Backup, console |
| **L2** | L1 + `settings.schema.json` | Auto-generated settings form |
| **L3** | L2 + mod support (provider and/or hook) | Mods tab with add / remove / update |

A level is declared in `adapter.json` and validated: Core refuses an adapter that claims a level without the required parts.

## 2. Folder layout

```
adapters/
  valheim/
    adapter.json            required
    settings.schema.json    required from L2
    guide.md                optional (required for L0); also shown as "Notes" at higher levels
    hooks/                  optional PowerShell scripts
      render-config.ps1
      install-mods.ps1
    icon.png                optional, 128×128
```

Built-in adapters are loaded read-only from `C:\Program Files\GameWarden\adapters-builtin\` and update with the binaries. The Owner's own adapters live in `C:\GameWarden\adapters\`; one with the same `id` overrides the built-in adapter (OQ-18).

## 3. `adapter.json`

| Field | Type | Req. | Description |
|---|---|---|---|
| `schemaVersion` | int | ✓ | Adapter format version. Starts at `1`. |
| `id` | string | ✓ | Lowercase, `[a-z0-9-]+`, unique. |
| `name` | string | ✓ | Display name. |
| `version` | semver | ✓ | Adapter version (not the game's). |
| `level` | `L0`–`L3` | ✓ | Declared support level. |
| `source` | object | L1 | How to get server files. See §3.1. |
| `launch` | object | L1 | `exe`, `args` (array), `workingDir`, `env`, `consoleLogFile`?. Values support templates (§4). `consoleLogFile` is for servers that write their console to a file instead of stdout (Unity's `-logFile`): the Runner deletes the file before the start and relays its lines as stdout, so the console view, the daily logs and `logRegex` readiness on `stdout` work unchanged. |
| `ports` | array | L1 | `{ "name", "protocol": "udp"\|"tcp", "default", "offset"? }` — `offset` ties a port to another (e.g. query = game + 1). |
| `readiness` | object | L1 | `{ "type": "logRegex"\|"port"\|"delay", "source"?, ... , "timeoutSec" }`. For `logRegex`, `source` is `"stdout"` (default) or `"file:<path>"` for servers that log to a file or buffer stdout (OQ-19). |
| `stop` | object | L1 | `{ "method": "ctrl-c"\|"stdin"\|"rcon"\|"rest"\|"kill", "command"?, "timeoutSec" }` |
| `backup` | object | L1 | `{ "paths": [...], "mode": "stop"\|"saveThenCopy"\|"hot", "saveCommand"? }` |
| `players` | object | – | Optional player probe: `{ "type": "a2s"\|"logRegex"\|"rcon"\|"rest", ... }` |
| `console` | object | – | `{ "stdin": bool, "rcon": bool, "operatorAllowlist": ["save", "broadcast *"] }` |
| `prerequisites` | array | – | Ids from [08 §2](08-operations.md#2-prerequisites): `vcredist-x64`, `vcredist-x86`, `directx-legacy`. Checked before every start (412 `prerequisite_missing` with the download link); an unknown id makes the adapter invalid. |
| `resources` | object | L1 | `{ "min": { "cpuCores", "ramMb", "diskMb" }, "recommended": { ... } }`. Checked in the create wizard and before every start: below `min` the start is blocked (412 `resources_insufficient`; the Owner may force it), below `recommended` it warns. CPU is compared with logical processors, RAM with free physical memory (other running instances already use theirs), disk with free space + 20 % (OQ-07, [ADR-0011](../decisions/0011-mvp-scope-and-direction.md)). |
| `savesRedirect` | object | – | If the game cannot be told where to save: `{ "from": "{paths.server}\\...\\Saved", "to": "{paths.data}" }` → Core creates a junction. |
| `mods` | object | L3 | `{ "provider": "thunderstore"\|"workshop"\|"manual"\|"hook", "loader"?, "community"? }` |
| `hooks` | object | – | `{ "renderConfig"?: "hooks/render-config.ps1", "installMods"?: ..., "preStart"?: ..., "postInstall"?: ... }` |

### 3.1 `source`

| `type` | Fields | Notes |
|---|---|---|
| `steamcmd` | `appId`, `branch`?, `branchPassword`?, `requiresLogin` (default `false`) | Anonymous only in MVP. |
| `zip` | `url` (template, may include `{version}`), `versionCheck`? | e.g. vanilla Terraria. |
| `manual` | — | L0 or games whose files the user copies in. |

## 4. Templates

Strings in `launch`, `readiness`, `stop`, `backup`, `savesRedirect` may contain placeholders:

| Placeholder | Value |
|---|---|
| `{instance.id}`, `{instance.displayName}` | Instance identity |
| `{paths.server}`, `{paths.config}`, `{paths.data}`, `{paths.mods}`, `{paths.logs}` | Instance folders |
| `{ports.<name>}` | Allocated port |
| `{settings.<key>}` | Setting value (validated by schema) |
| `{secrets.<key>}` | Secret value; Core redacts it from logs and from the displayed command line |

The settings and secrets an instance needs are the ones its `launch` and `readiness` templates use; a start with any of them missing is refused with the CLI command that sets it (`gamewarden set <id> <key>=...`, `gamewarden secret <id> <key>`). The Runner deletes `launchSpec.json` as soon as it has read it, because rendered arguments may contain secrets.

Security rules:

- `launch.args` is an **array**; Core passes it with `ProcessStartInfo.ArgumentList` (no shell), so a setting value can never inject extra arguments or commands.
- Values written to config files are escaped by the target writer (§5.2).
- Hooks receive values in a JSON context file, never interpolated into a command line.

## 5. `settings.schema.json` (L2)

### 5.1 Setting definition

```json
{
  "settings": [
    {
      "key": "maxPlayers",
      "label": "Max players",
      "type": "int",
      "default": 10,
      "min": 1,
      "max": 64,
      "group": "General",
      "restartRequired": true,
      "target": { "kind": "ini", "file": "{paths.server}\\Pal\\Saved\\Config\\WindowsServer\\PalWorldSettings.ini", "section": "...", "key": "..." }
    }
  ]
}
```

| Field | Notes |
|---|---|
| `type` | `string`, `int`, `number`, `bool`, `enum` (`options[]`), `secret` |
| Validation | `min`, `max`, `pattern`, `maxLength`, `required` |
| UI | `label`, `help`, `group`, `advanced` (collapsed), `order` |
| `restartRequired` | Panel shows "Restart to apply" after save |
| `visibleTo` | Optional minimum permission to see the field (default: `instance.settings.read`) |

### 5.2 Targets

| `kind` | Writes to |
|---|---|
| `arg` | Nothing written; value is used via `{settings.key}` in `launch.args` |
| `ini` | INI key in a section |
| `json` | JSON path |
| `keyvalue` | `key=value` text files (e.g. Terraria `serverconfig.txt`) |
| `env` | Environment variable of the game process |
| `hook` | The adapter's `renderConfig` hook writes it (formats that don't fit the above) |

Rendering happens before every start and after every settings save; Core keeps a copy of the rendered files in `{paths.config}` for diffing.

## 6. Hooks contract

| Aspect | Rule |
|---|---|
| Language | Windows PowerShell 5.1 (`powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File <hook> -Context <ctx.json>`) |
| Identity | `gamewarden-run`, the same as the game process ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)) |
| Input | `ctx.json`: instance, paths, ports, settings, secrets (only the ones the hook declares), adapter info |
| Output | stdout lines; lines starting with `##gw ` are commands: `##gw progress 40 Downloading BepInEx`, `##gw warn <text>`, `##gw result <json>` |
| Exit code | `0` success; anything else fails the job stage |
| Timeout | Per hook in `adapter.json` (default 300 s) |
| Working dir | Adapter folder (read-only to hooks by convention); write only inside instance paths |

## 7. Mods

| Provider | Mechanism | Status |
|---|---|---|
| `thunderstore` | Public package API; resolves dependencies; downloads to `{paths.mods}\cache`, installs per `loader` rules | Planned first (Valheim / BepInEx) |
| `workshop` | `steamcmd +workshop_download_item <appId> <itemId>` | Planned |
| `curseforge` | Needs an API key **[Unverified: key requirement/terms]** | Later |
| `nexus` | Direct API downloads need a Premium account **[Unverified]** | Guide only (`manual`) |
| `manual` | Panel shows the guide + a mod list the admin maintains | Always available |
| `hook` | Adapter's `installMods` hook does it | For special cases |

Mod list per instance is stored in `{paths.mods}\mods.json` (`{ id, version, pinned, enabled }`), so the same list can be shared with players (Share button shows required client mods).

## 8. Validation and trust

- Core validates every adapter at load against a JSON Schema (`schemaVersion`); invalid adapters are listed with errors and cannot be used.
- Instances record `adapterVersion`; when an adapter is updated, the panel shows "adapter updated" and re-renders configs on next start.
- **Only the Owner** can add or change adapters — hooks are code that runs on the VM ([06-security](06-security-and-access.md#7-adapters-hooks-and-mods-are-code)).

## 9. Example: Valheim (L1 → L3)

> Game-specific values below are from memory and are **[Unverified]** until checked in milestone M0.
>
> Confirmed 2026-10-27 from the official Valheim dedicated server guide: the server uses the given port and port + 1; `-savedir` overrides where worlds and the admin/banned/permitted lists are stored (so it is always passed, [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)); the server is stopped with CTRL+C.

```json
{
  "schemaVersion": 1,
  "id": "valheim",
  "name": "Valheim",
  "version": "0.1.0",
  "level": "L3",
  "source": { "type": "steamcmd", "appId": 896660 },
  "launch": {
    "exe": "{paths.server}\\valheim_server.exe",
    "args": [
      "-nographics", "-batchmode",
      "-name", "{settings.serverName}",
      "-port", "{ports.game}",
      "-world", "{settings.world}",
      "-password", "{secrets.password}",
      "-savedir", "{paths.data}",
      "-saveinterval", "600",
      "-public", "0",
      "-logFile", "{paths.logs}\\valheim_server.log"
    ],
    "workingDir": "{paths.server}",
    "env": { "SteamAppId": "892970" },
    "consoleLogFile": "{paths.logs}\\valheim_server.log"
  },
  "ports": [
    { "name": "game",  "protocol": "udp", "default": 2456 },
    { "name": "query", "protocol": "udp", "offset": { "of": "game", "by": 1 } }
  ],
  "readiness": { "type": "logRegex", "source": "stdout", "pattern": "Game server connected|Opened Steam server", "timeoutSec": 300 },
  "stop": { "method": "ctrl-c", "timeoutSec": 60 },
  "backup": { "paths": ["{paths.data}"], "mode": "stop" },
  "players": { "type": "a2s", "port": "{ports.query}" },
  "prerequisites": ["vcredist-x64"],
  "resources": {
    "min": { "cpuCores": 2, "ramMb": 2048, "diskMb": 2000 },
    "recommended": { "cpuCores": 4, "ramMb": 4096, "diskMb": 4000 }
  },
  "mods": { "provider": "thunderstore", "community": "valheim", "loader": "bepinex" }
}
```

Notes:

- `-public 0` keeps the server off the public list (VPN use).
- `-saveinterval 600` (Valheim's default is 1800 s) bounds what a hard power-off can lose (OQ-32) **[value to confirm in S2]**; at L2 it becomes a setting.
- Valheim requires a password of at least 5 characters that is not part of the server name **[Unverified]** → schema `pattern`.
- The `resources` numbers are placeholders **[Unverified]** until spike S2 measures the server on the template VM.
- Community guides say the shipped `start_headless_server.bat` sets `SteamAppId=892970` **[Unverified]**; the built-in adapter (0.2.0) sets it, as the batch file does. Check on the VM that the server starts with it.
- The built-in adapter passes `-logFile` and relays it with `consoleLogFile`, because Unity players on Windows may not write their log to a redirected stdout **[Unverified: whether Valheim does; the file path works either way]**. Readiness also accepts `Opened Steam server` in case `Game server connected` never appears without a Steam connection **[Unverified]**.

## 10. Other planned adapters (notes)

| Game | Source | Notable adapter work |
|---|---|---|
| **Palworld** | `steamcmd` appId 2394010 **[Unverified]** | `PalWorldSettings.ini` stores all options in one `OptionSettings=(...)` line → needs a `renderConfig` hook. Stop via admin REST API if enabled, else `ctrl-c` **[Unverified]**. High RAM usage **[Unverified]**; set `resources.min.ramMb` and `resources.recommended.ramMb` accordingly. |
| **Terraria (vanilla)** | `zip` from terraria.org **[Unverified]** | `keyvalue` config (`serverconfig.txt`); `stop.method = stdin`, command `exit`; console commands via stdin. |
| **tModLoader** | `steamcmd` appId 1281930 **[Unverified]** | Mods via Workshop. |
