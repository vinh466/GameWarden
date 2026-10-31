# 04 · Adapter specification

An **adapter** teaches GameWarden one game. It is mostly declarative (JSON); code (PowerShell hooks) is only for what JSON cannot express. Core implements everything generic (process control, ports, firewall, backups, jobs, UI).

## 1. Support levels

| Level | Adapter provides | Panel shows |
|---|---|---|
| **L0** | `guide.md` only | Step-by-step guide; no automation |
| **L1** | install source, launch, stop, readiness, backup paths | Install / Start / Stop / Restart / Backup, console |
| **L2** | L1 + `settings.schema.json` | Auto-generated settings form |
| **L3** | L2 + `mods`: a loader Core has and a source (§7) | Mods tab with add / remove / update |

A level is declared in `adapter.json` and validated: Core refuses an adapter that claims a level without the required parts.

## 2. Folder layout

```
adapters/
  valheim/
    adapter.json            required
    settings.schema.json    required from L2
    help.json               optional, needs settings.schema.json: longer explanations with sources (§5.3)
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
| `ports` | array | L1 | `{ "name", "protocol": "udp"\|"tcp", "default", "offset"?, "internal"? }` — `offset` ties a port to another (e.g. query = game + 1). `internal: true` is allocated and checked for clashes like any other port, but gets no firewall rule: only Core uses it, on loopback (ADR-0022 decision 4). |
| `files` | array | – | Config files Core renders before every start (ADR-0022 decision 1): `{ "name", "path", "template", "format", "section"?, "field"?, "fixed"? }`. `path` and `template` are templates; `template` is the game's own default file, read after install. The first format is `ue-struct-ini`: `section` and `field` name the INI section and the `Key=(A=1,B="x")` line. `fixed` maps a field to a raw value (templates allowed, quotes included), written before the settings; a field no setting names keeps the template's value. The result is written as `gamewarden-run` and a copy stays in `{paths.config}`. |
| `readiness` | object | L1 | `{ "type": "logRegex"\|"port"\|"delay"\|"http", "source"?, ... , "timeoutSec" }`. For `logRegex`, `source` is `"stdout"` (default) or `"file:<path>"` for servers that log to a file or buffer stdout (OQ-19). For `http`: `url` (template), `status` (default 200), `auth`? (`{ "username", "password" }`, both templates, e.g. `{secrets.AdminPassword}`), `intervalSec`? (default 2). A 401, 403 or other 4xx fails the start at once; a connection error or 5xx means "still starting" (ADR-0022 decision 3). |
| `stop` | object | L1 | `{ "method": "ctrl-c"\|"stdin"\|"rcon"\|"rest"\|"http"\|"kill", "command"?, "timeoutSec", "requests"?, "auth"? }`. For `http`: `requests` is a list of `{ "method", "url", "body"?, "timeoutSec"? }` sent in order (Palworld: save, then shutdown), with one `auth` for all; a failed request leaves the game to `timeoutSec`, then Core kills it (ADR-0022 decision 3). |
| `backup` | object | L1 | `{ "paths": [...], "mode": "stop"\|"saveThenCopy"\|"hot", "saveCommand"? }` |
| `players` | object | – | Optional player probe: `{ "type": "a2s"\|"logRegex"\|"rcon"\|"rest", ... }` |
| `console` | object | – | `{ "stdin": bool, "rcon": bool, "operatorAllowlist": ["save", "broadcast *"] }` |
| `prerequisites` | array | – | Ids from [08 §2](08-operations.md#2-prerequisites): `vcredist-x64`, `vcredist-x86`, `directx-legacy`. Checked before every start (412 `prerequisite_missing` with the download link); an unknown id makes the adapter invalid. |
| `resources` | object | L1 | `{ "min": { "cpuCores", "ramMb", "diskMb" }, "recommended": { ... } }`. Checked in the create wizard and before every start: below `min` the start is blocked (412 `resources_insufficient`; the Owner may force it), below `recommended` it warns. CPU is compared with logical processors, RAM with free physical memory (other running instances already use theirs), disk with free space + 20 % (OQ-07, [ADR-0011](../decisions/0011-mvp-scope-and-direction.md)). |
| `savesRedirect` | object | – | If the game cannot be told where to save: `{ "from": "{paths.server}\\...\\Saved", "to": "{paths.data}" }` → Core creates a junction. |
| `mods` | object | L3 | `{ "loader": "bepinex", "loaderPackage"?: "denikson-BepInExPack_Valheim", "sources": [{ "type": "thunderstore", "community": "valheim" }] }` ([13 §4.1](13-mods.md#41-source-loader-mod-set)). Required at L3 and only there. `loader` must be a loader Core has (`bepinex` since phase 1b, §7); `loaderPackage` is the loader's own package (`Namespace-Name`), added to every set like a dependency; `sources` needs at least one entry, and `thunderstore` (the only type so far) needs a `community`. |
| `hooks` | object | – | `{ "renderConfig"?: "hooks/render-config.ps1", "installMods"?: ..., "preStart"?: ..., "postInstall"?: ... }` |

### 3.1 `source`

| `type` | Fields | Notes |
|---|---|---|
| `steamcmd` | `appId`, `branch`?, `branchPassword`?, `requiresLogin` (default `false`) | Anonymous only in MVP. |
| `zip` | `url` (template, may include `{version}`), `versionCheck`? | e.g. vanilla Terraria. |
| `manual` | — | L0 or games whose files the user copies in. |

## 4. Templates

Strings in `launch`, `readiness`, `stop`, `files` (path, template and fixed values), `backup`, `savesRedirect` may contain placeholders:

| Placeholder | Value |
|---|---|
| `{instance.id}`, `{instance.displayName}` | Instance identity |
| `{paths.server}`, `{paths.config}`, `{paths.data}`, `{paths.mods}`, `{paths.logs}` | Instance folders |
| `{ports.<name>}` | Allocated port |
| `{settings.<key>}` | Setting value (validated by schema) |
| `{secrets.<key>}` | Secret value; Core redacts it from logs and from the displayed command line |

The settings and secrets an instance needs are the ones its `launch`, `readiness` and `stop` templates use, less the settings with a default in `settings.schema.json` and plus its `required` ones (§5.1); a start with any of them missing is refused with the CLI command that sets it (`gamewarden set <id> <key>=...`, `gamewarden secret <id> <key>`). A secret used only by a fixed field of a declared file is not required that way: when it is unset the field is written as an empty value, which keeps an optional password (Palworld's `ServerPassword`) optional. The Runner deletes `launchSpec.json` as soon as it has read it, because rendered arguments may contain secrets.

Security rules:

- `launch.args` is an **array**; Core passes it with `ProcessStartInfo.ArgumentList` (no shell), so a setting value can never inject extra arguments or commands.
- A string, list item or secret written into a config file may not contain a double quote or a line break: Core refuses the value instead of escaping it (ADR-0022 decision 2). List items are also checked against the list's options.
- Hooks receive values in a JSON context file, never interpolated into a command line.

## 5. `settings.schema.json` (L2)

The settings an adapter offers, in groups. The panel builds the new server wizard and the Settings tab from it, one section per group ([11 §4](11-web-panel.md#4-screens)); Core checks values against it when an instance is created and when settings are saved, fills the launch section with its defaults, and passes each value on by its target. Values are stored as text (`true`, `600`, `1.5`, `normal`, `Steam,Xbox`). Implemented 2026-10-29 with the `arg` target for Valheim (§9); the `file` target, the `number`/`list` types and generated secrets arrived with the Core model of ADR-0022 (2026-10-31), before Palworld's adapter (§10).

```json
{
  "groups": [
    { "id": "server", "label": { "en": "Server", "vi": "Server" }, "help": "How the server shows up in Valheim." },
    { "id": "saves", "label": "Saving", "advanced": true }
  ],
  "settings": [
    { "key": "serverName", "type": "string", "group": "server", "label": "Name in Valheim", "required": true, "maxLength": 64 },
    { "key": "password", "type": "secret", "group": "server", "label": "Password", "required": true, "minLength": 5 },
    {
      "key": "crossplay", "type": "bool", "group": "server", "default": false,
      "warning": { "en": "Crossplay goes through PlayFab's relay, not through the VPN.", "vi": "…" },
      "target": { "kind": "arg", "args": ["-crossplay"] }
    },
    {
      "key": "preset", "type": "enum", "group": "server", "default": "normal", "wizard": true,
      "options": [{ "value": "normal", "label": "Normal" }, { "value": "hard", "label": { "en": "Hard", "vi": "Khó" } }],
      "target": { "kind": "arg", "args": ["-preset", "{settings.preset}"] }
    },
    { "key": "saveInterval", "type": "int", "group": "saves", "default": 600, "min": 60, "max": 7200 }
  ]
}
```

### 5.1 Groups and settings

| Field | Notes |
|---|---|
| `groups[]` | `id` (`[a-z][a-zA-Z0-9-]*`), `label`, `help`, `advanced` (closed until opened in the Settings tab). The order of the groups is the order of the sections |
| `key` | 1–64 letters, digits, `_` or `-`; `{settings.<key>}` in templates, `{secrets.<key>}` for a secret |
| `type` | `string`, `int`, `bool`, `enum` (`options[]` of `value` and `label`), `number` (a decimal), `list` (options: a multi-select, stored as `Steam,Xbox` and written as `(Steam,Xbox)`; `quoted: true` without options: free text, edited as tags and written as `("A","B")`), `secret` (write-only; no default and no target, the launch section or a declared file's `fixed` fields use it) |
| `group` | One of `groups[].id` |
| Text | `label`, `help`, `warning` (shown with the field whatever its value) and option labels: a string (English) or `{ "en": …, "vi": … }` (docs/11 D1). A longer explanation with its sources goes in the adapter's `help.json`, opened from the field ([12 §5.1](12-explanations.md#51-where-the-text-lives)) |
| `default` | JSON value, used when nothing is stored (an empty value falls back to it too). A setting with a default is never required |
| `required` | Must have a value before a start. Required as well: each `{settings.*}` the launch section uses without a default, and each `{secrets.*}` it uses |
| `wizard` | Asked in the new server wizard. Required settings always are; the others keep their default until changed in the Settings tab |
| Validation | `min`, `max` (`int`, `number`); `step` (`number`); `minLength`, `maxLength`, `pattern` (`string`, `secret`). A `list` default is an array and its stored text is comma-separated; a list with options accepts only those, while a `quoted` list takes free text (no quote or line break). Core refuses a value that does not fit (400 `validation_failed`); the panel checks the same rules while typing. Keys the schema does not know are accepted as before (§4) |
| `generate` | `secret` only: Core generates a value when an instance is created without one (ADR-0022 decision 5). The Owner may replace it in the Settings tab, and the new-server wizard does not ask for it |
| `quoted` | `list` only: free-text items, written in quotes (`("A","B")`), for Palworld's `DenyTechnologyList`; a list with options is a multi-select instead |
| `restartRequired` | Default `true`: the panel shows "Restart to apply" after a save |
| `visibleTo` | Minimum permission to see the field (M6) |

Core refuses a schema when a group or key repeats, a setting names an unknown group or type, a default does not fit its setting, a secret has a default or a target, a list has neither options nor `quoted`, or a template uses a `{settings.*}` or `{secrets.*}` the schema does not define. A schema makes the adapter L2 or higher, and L2 needs one (§1).

### 5.2 Targets

| `kind` | Writes to | Status |
|---|---|---|
| (none) | Nothing: the value reaches the game through `{settings.key}` in the launch section, with the default when nothing is stored | Implemented |
| `arg` | `args` (templates) after `launch.args`, in schema order, when the value differs from the default; for a `bool`, when it is true, and its `offArgs` (`bool` only) when it is false, for a game whose own default is on (Valheim's `-public 0`). What the game always needs goes in the launch section instead (Valheim's `-saveinterval`) | Implemented |
| `file` | `{ "kind": "file", "file": "<files[].name>", "field"?: "<field>" }`: the field (default: the setting's key) of a declared file, rendered before every start (ADR-0022). A setting may not target a field that `files[].fixed` names | Implemented (`ue-struct-ini`) |
| `ini` | INI key in a section | M5, when a format no `files` format covers needs one |
| `json` | JSON path | M5 |
| `keyvalue` | `key=value` text files (e.g. Terraria `serverconfig.txt`) | M5 |
| `env` | Environment variable of the game process | M5 |
| `hook` | The adapter's `renderConfig` hook writes it (formats that don't fit the above) | M5 |

A declared file is rendered before every start, as `gamewarden-run`; Core keeps a copy of the rendered files in `{paths.config}` for diffing. A field the template has and no setting or fixed field names keeps the game's value, so a field a newer game version added still gets the game's default. A field a setting names but the template lacks is appended, which a test catches before a release.

### 5.3 `help.json`

The longer explanations the panel opens in its help drawer, next to a field or a section title, with their sources ([12](12-explanations.md), content model [12 §5](12-explanations.md#5-content-model)). Optional; an adapter with `help.json` needs `settings.schema.json`, whose settings it explains. Implemented 2026-10-29; Valheim has one (§9).

```json
{
  "topics": {
    "saveInterval": {
      "summary": "How often Valheim saves the world, in seconds.",
      "body": [
        { "p": "Everything since the last save is lost when the machine loses power." },
        { "tip": "GameWarden accepts 60 to 7200 seconds." }
      ],
      "tested": "What a VM walk-through or a test showed, with its date.",
      "verified": true,
      "sources": [
        { "title": "A Guide to Dedicated Servers", "url": "https://valheimgame.com/support/a-guide-to-dedicated-servers/", "publisher": "Iron Gate Studio", "checked": "2026-10-29" }
      ],
      "related": ["preset"]
    },
    "preset": {
      "summary": "A preset sets all world modifiers at once.",
      "body": [{ "p": "…" }],
      "values": [{ "value": "normal", "text": "The game as designed." }, { "value": "hard", "text": "…" }],
      "verified": false,
      "sources": [{ "title": "…", "url": "https://…", "checked": "2026-10-29" }]
    }
  }
}
```

| Field | Notes |
|---|---|
| Topic key | A setting key (`saveInterval`), `group:<group id>` (`group:modifiers`) or `option:<setting key>=<value>` for one option of an enum setting (`option:preset=hard`). The key must exist in `settings.schema.json` |
| `title` | Optional; the panel uses the label of the setting, group or option |
| `summary` | Required: one or two sentences, the short answer |
| `body` | Required, at least one block. A block is exactly one of `{ "p": text }`, `{ "list": [text, …] }` (not empty), `{ "tip": text }` or `{ "warning": text }`. Plain text only: no markdown and no HTML ([11 §10](11-web-panel.md#10-working-rules)) |
| `values` | Only on an enum setting's own topic: `{ value, text }` per option, each `value` an option of the setting, none twice. The panel marks the default from the schema |
| `tested` | Optional: what a VM walk-through or a test showed, with its date |
| `verified` | Required: `false` while any statement rests on an unverified source; the panel then shows "Not verified yet" |
| `sources` | Required, at least one: `title`, `url` (absolute `https`), `publisher` (optional), `checked` (`yyyy-MM-dd`, the day the source was last checked, not in the future) |
| `related` | Optional: other topic keys of the same file (not the topic itself), opened in the same drawer |

Text is English plain strings ([12 §3](12-explanations.md#3-user-experience)); unknown fields are refused, so a misspelt field is an error rather than lost text. `GET /adapters` sets `hasHelp` on each setting and group that has a topic; the panel fetches the topics with `GET /adapters/{id}/help` when it opens the drawer ([05](05-api.md#adapters)).

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

The design is [13](13-mods.md) and [ADR-0020](../decisions/0020-mods-in-set-folders-as-pinned-sets.md), accepted on 2026-10-30. An adapter's `mods` section (§3) names a **loader** and its **sources**; Core builds each mod set into its own folder, `{paths.mods}\sets\<id>\`, and SQLite keeps the sets ([13 §4.2](13-mods.md#42-data)). `{paths.mods}\mods.json` is only a copy of the active set.

| Source | Mechanism | Status |
|---|---|---|
| `thunderstore` | Package API of the adapter's `community`; exact dependency versions; zips in `{paths.mods}\cache\` with one trusted SHA-256 per version | Since phase 1a (Valheim) |
| `manual` | An uploaded zip | Later (Palworld, [13 §4.13](13-mods.md#413-other-games)) |
| `workshop` | `steamcmd +workshop_download_item` | Later |

A Thunderstore package that is not listed in the adapter's `community` is installed with a warning in the job log. A package that unpacks to more than `ModPackageMaxBytes` (2 GiB by default, [08 §6](08-operations.md#6-configuration)), counted as it is written, fails the build.

A loader is code in Core, not an adapter hook. **`bepinex`** (Unity Doorstop 4 and BepInEx 5, phase 1b):

- **Set folder**, following r2modman's install rules for Valheim (spike S-M2, [13 §5](13-mods.md#5-spikes)):
  - the loader package's `BepInEx\**` into `BepInEx\`, its `winhttp.dll` and `doorstop_config.ini` into `loader\`;
  - a package's `plugins`, `patchers`, `monomod`, `core` and `SlimVML` folders into `BepInEx\<folder>\<Namespace-Name>\`, its `config` folder into `BepInEx\config\`;
  - any other file into `BepInEx\plugins\<Namespace-Name>\` under its own name (`*.mm.dll` into `monomod`), Thunderstore's `manifest.json`, `icon.png` and readme included.
- **Entry files**: before a start with *Mods on*, `winhttp.dll` and `doorstop_config.ini` are copied from the active set's `loader\` into `{paths.server}` when their SHA-256 differs; the copy of the ini says `enabled = false`.
- **Launch arguments**, after the adapter's own: *Mods on* adds `--doorstop-enabled true --doorstop-target-assembly {paths.mods}\sets\<active>\BepInEx\core\BepInEx.Preloader.dll`; *Mods off* adds `--doorstop-enabled false` once the entry files are in `server\` (a server that never had mods gets no extra arguments).
- **Configs**: `BepInEx\config\`, carried over from the set that ran before at a set's first start ([13 §4.7](13-mods.md#47-configs)).

## 8. Validation and trust

- Core validates every adapter at load against a JSON Schema (`schemaVersion`); invalid adapters are listed with errors and cannot be used.
- `help.json` is validated with the adapter (§5.3), and an invalid one makes the adapter invalid: a topic key that names no setting, group or option of the schema; an empty `title`, `summary` or `tested`; an empty `body`, a block with no kind or several, an empty `list` or list item; `values` on anything but an enum setting's topic, or a value that is not an option; no `sources`, a URL that is not absolute `https`, a `checked` date that is not `yyyy-MM-dd` or is in the future (Core compares with today in the latest time zone, UTC+14); a `related` key that is not a topic of the file; an unknown field; `help.json` without `settings.schema.json`.
- The ADR-0022 parts are validated too: a declared file needs a unique name, a known `format`, a template, and for `ue-struct-ini` a `section` and a `field`; an `http` readiness needs a `url` and a status from 100 to 599; an `http` stop needs at least one request with a known method, a URL and a timeout; basic auth needs a username and a password; a `number` needs a positive `step` and may use `min`/`max`; a `list` needs options unless it is `quoted`, and `quoted` is for a list only; `generate` is for secrets only; a `file` target must name a declared file and may not name a fixed field; every placeholder in a fixed value, an http URL, body or auth must be defined in the schema, and a secret may appear there (it may not, however, carry a quote or a line break).
- Instances record `adapterVersion`; when an adapter is updated, the panel shows "adapter updated" and re-renders configs on next start.
- **Only the Owner** can add or change adapters — hooks are code that runs on the VM ([06-security](06-security-and-access.md#7-adapters-hooks-and-mods-are-code)).

## 9. Example: Valheim (L2)

The built-in adapter: [`adapters/valheim/adapter.json`](../adapters/valheim/adapter.json) and [`settings.schema.json`](../adapters/valheim/settings.schema.json) (L2 since 2026-10-29; [`help.json`](../adapters/valheim/help.json) since 0.3.1, §5.3; L3 since 0.4.0, 2026-10-30, with the `mods` section below). Its launch section:

```json
"launch": {
  "exe": "{paths.server}\\valheim_server.exe",
  "args": [
    "-nographics", "-batchmode",
    "-name", "{settings.serverName}",
    "-port", "{ports.game}",
    "-world", "{settings.world}",
    "-password", "{secrets.password}",
    "-savedir", "{paths.data}",
    "-saveinterval", "{settings.saveInterval}",
    "-logFile", "{paths.logs}\\valheim_server.log"
  ],
  "workingDir": "{paths.server}",
  "env": { "SteamAppId": "892970" },
  "consoleLogFile": "{paths.logs}\\valheim_server.log"
}
```

The server's launch arguments, researched 2026-10-29 from server hosts' guides, a widely used Docker image and patch notes (sources in pull request #19; the official references are `Valheim Dedicated Server Manual.pdf` in the server folder and Iron Gate's [A Guide to Dedicated Servers](https://valheimgame.com/support/a-guide-to-dedicated-servers/), checked 2026-10-29; the explanations with their sources are in `adapters/valheim/help.json`, §5.3):

| Argument | Values (game default) | In the adapter |
|---|---|---|
| `-name`, `-world`, `-password` | Text; the password at least 5 characters and not part of the name | Settings `serverName`, `world`; secret `password` (`minLength` 5) |
| `-port` | Port; the server also uses port + 1 | GameWarden allocates `game` and `query` |
| `-savedir`, `-logFile`, `-nographics`, `-batchmode` | | Fixed |
| `-public` | `0` / `1` (`1`): `1` lists the server in Valheim's server browser, `0` hides it so players join with *Join IP* (Iron Gate's guide, checked 2026-10-29) | Setting `public` (since 0.3.2, at the Owner's request), on by default since 0.3.4 (OQ-38): on passes `-public 1`, off passes `-public 0` through `offArgs`; a warning says the name is then visible to every player while the firewall rules still scope the game ports to the VPN ranges |
| `-saveinterval` | Seconds (1800) | Setting `saveInterval`, default 600 (OQ-32) |
| `-backups`, `-backupshort`, `-backuplong` | Count, seconds, seconds (4, 7200, 43200) | Settings in the advanced group *Saving* |
| `-crossplay` | Flag (off) | Setting `crossplay` with a warning: PlayFab's relay bypasses the VPN and the firewall rules (Iron Gate's guide, checked 2026-10-29: the relay needs no port forwarding; that the VPN and the firewall then no longer decide who reaches the server follows from it **[Unverified on its own]**); one Docker image also opens game port + 2 for it **[Unverified]** |
| `-preset` | `normal`, `casual`, `easy`, `hard`, `hardcore`, `immersive`, `hammer` (all seven confirmed by Iron Gate's guide, checked 2026-10-29; what each preset changes is documented only by the community) | Setting `preset`, asked in the wizard |
| `-modifier <group> <value>` | `combat` veryeasy … veryhard; `deathpenalty` casual … hardcore; `resources` muchless … most; `raids` none … muchmore; `portals` casual, hard, veryhard | One setting each; `normal` (no argument) is the default |
| `-setkey <key>` | `nobuildcost`, `playerevents`, `passivemobs`, `nomap` | One switch each |
| `-instanceid` | Text: several crossplay servers behind one address (Iron Gate's guide, checked 2026-10-29) | Not offered |

Notes:

- Whether an existing world keeps a modifier or key after its argument is removed is unclear (sources disagree): OQ-35, checked on the VM ([10 §8.4](10-build-test-release.md#84-walk-through-for-valheim-settings-l2)).
- The `resources` numbers are placeholders **[Unverified]** until spike S2 measures the server on the template VM.
- Community guides say the shipped `start_headless_server.bat` sets `SteamAppId=892970` **[Unverified]**; the built-in adapter sets it, as the batch file does. 2026-10-28, v0.1.0-rc.1 on the template VM: with this launch section (env, `-logFile` relayed by the Runner, the readiness pattern) Valheim reached Running, a friend joined, and CTRL+C saved the world on stop and on reboot. Which readiness alternative matched was not recorded.
- The built-in adapter passes `-logFile` and relays it with `consoleLogFile`, because Unity players on Windows may not write their log to a redirected stdout **[Unverified: whether Valheim does; the file path works either way]**. Readiness also accepts `Opened Steam server` in case `Game server connected` never appears without a Steam connection **[Unverified]**.
- Confirmed 2026-10-27 from the official Valheim dedicated server guide: the server uses the given port and port + 1; `-savedir` overrides where worlds and the admin/banned/permitted lists are stored (so it is always passed, [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)); the server is stopped with CTRL+C.
- Since 0.4.0 (L3, [13](13-mods.md)): `"mods": { "loader": "bepinex", "loaderPackage": "denikson-BepInExPack_Valheim", "sources": [{ "type": "thunderstore", "community": "valheim" }] }`. With *Mods on* the Doorstop arguments of §7 follow the arguments above.

## 10. Other planned adapters (notes)

| Game | Source | Notable adapter work |
|---|---|---|
| **Palworld** | `steamcmd` appId 2394010 (spike S-P1, 2026-10-31) | Design: [14](14-palworld.md), [ADR-0022](../decisions/0022-config-files-rendered-by-core-servers-controlled-over-http.md) (accepted 2026-10-31). Built 2026-10-31: the Core model (phase 1) and the adapter `palworld` 0.1.0, L2 (phase 2). `PalWorldSettings.ini` stores all 122 options in one `OptionSettings=(...)` line → rendered by Core (`ue-struct-ini`), not a hook; 116 settings + 2 secrets. Stopped by REST save and shutdown; ready when REST `info` answers. 16 GB RAM recommended. S-P2a (2026-10-31) verified the enum values, the quoted `DenyTechnologyList` and crossplay against the real server. |
| **Terraria (vanilla)** | `zip` from terraria.org **[Unverified]** | `keyvalue` config (`serverconfig.txt`); `stop.method = stdin`, command `exit`; console commands via stdin. |
| **tModLoader** | `steamcmd` appId 1281930 **[Unverified]** | Mods via Workshop. |
