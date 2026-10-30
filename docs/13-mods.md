# 13 · Mods: Valheim first, shared with r2modman codes

**Status: Proposed (2026-10-30).** The Owner put mods next ([ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)) and answered the first questions on 2026-10-30 (§2). The decision it asks for is [ADR-0020](../decisions/0020-mods-in-set-folders-as-pinned-sets.md). Adapter spec: [04 §7](04-adapter-spec.md#7-mods); run-as user and folder rights: [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md), [ADR-0009](../decisions/0009-data-root-acl-and-secrets.md).

## 1. Goal

The Owner and their friends play Valheim with mods, installed with r2modman on each PC. The server needs the same mods at the same versions, and today it is set up by hand. Requirements:

- The Owner gives a server its mods by pasting an **r2modman profile code**, the way the group already shares mods, or by adding Thunderstore packages one by one.
- Friends get the server's mods as an r2modman code, so their versions match the server's.
- Every mod has an exact version. Nothing updates by itself; updating is a button.
- The game folder stays as SteamCMD installed it. *Mods on / off* is one switch.
- The model fits more games: Palworld comes next ([ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)) and needs other sources and another loader.

## 2. The Owner's answers (2026-10-30)

| # | Question | Answer |
|---|---|---|
| 1 | Use r2modman codes? | Yes, both ways: the friends use r2modman, which makes it convenient |
| 2 | Mods already installed by hand on the VM? | None, so no step to take over a hand-made install |
| 3 | Update mods automatically? | No: off, the Owner presses *Update* |
| 4 | Configs in the first version? | Imported from a code and viewable; editing in the panel later |
| 5 | Client-only mods in a code? | Install every mod of the code on the server; no filtering |

## 3. Facts (checked 2026-10-30)

- **BepInEx for Valheim.** `denikson-BepInExPack_Valheim` 5.4.2360, published 2026-10-28 (BepInEx 5.4.23.6, Unity Doorstop 4.6.0), pinned on Thunderstore. In its zip the files sit under `BepInExPack_Valheim/`: `winhttp.dll`, `doorstop_config.ini`, `BepInEx/core/…`, `BepInEx/config/BepInEx.cfg`. On Windows, `winhttp.dll` next to the game's exe is a proxy DLL (Doorstop) that loads BepInEx; the pack's README says to just run the game. Sources: [the package's API entry](https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/) and its zip.
- **How r2modman starts a modded game.** It keeps BepInEx and the mods in a profile folder and passes Doorstop arguments: `--doorstop-enabled true --doorstop-target-assembly <profile>\BepInEx\core\BepInEx.Preloader.dll` (Doorstop 4), and `--doorstop-enabled false` for vanilla ([r2modmanPlus `BepInExGameInstructions.ts`](https://github.com/ebkr/r2modmanPlus/blob/develop/src/r2mm/launching/instructions/instructions/loader/BepInExGameInstructions.ts)). **[Unverified: that this works for `valheim_server.exe`, spike S-M1]**
- **r2modman profile codes.** A code is the key of a text that Thunderstore stores: `#r2modman` and a newline, then the base64 of a zip. r2modman creates it with `POST https://thunderstore.io/api/experimental/legacyprofile/create/` (answer `{ key }`) and reads it with `GET …/legacyprofile/get/{key}/`. The zip holds `export.r2x` (YAML: `profileName`, `mods[]` of `name` = `Namespace-Name`, `version { major, minor, patch }`, `enabled`) and `config/` (the profile's `BepInEx/config`). An old code can answer 404 ("might be expired"), and the API answers 429 when called too often. Sources: r2modmanPlus [`ProfilesClient.ts`](https://github.com/ebkr/r2modmanPlus/blob/develop/src/r2mm/profiles/ProfilesClient.ts), [`ProfileModList.ts`](https://github.com/ebkr/r2modmanPlus/blob/develop/src/r2mm/mods/ProfileModList.ts), [`ExportMod.ts`](https://github.com/ebkr/r2modmanPlus/blob/develop/src/model/exports/ExportMod.ts). **[Unverified: how long a code lives]**
- **Thunderstore.** One package: `GET https://thunderstore.io/api/experimental/package/{namespace}/{name}/` (latest version, deprecated and pinned flags, categories, review status). A version's zip: `https://thunderstore.io/package/download/{namespace}/{name}/{version}/`. Dependencies are exact: `Namespace-Name-1.2.3`. Every zip has a `manifest.json`. The Valheim list, `GET https://thunderstore.io/c/valheim/api/v1/package/`, had 14,108 packages and 108,270 versions, 14.5 MB compressed, downloaded in 1.5 s; each version has `file_size`, but **no hash**. No published rate limit or terms for automated downloads were found **[Unverified]**.
- **The group's mods** (the Owner's `valheim_mods_wiki` workspace): Epic Loot (RandyKnapp; latest 0.15.4, which needs Jotunn, JsonDotNET and BepInExPack) and VikingNPC Continued (Norsemen). Both must be on the server and on every client at the same version (Jötunn's `EveryoneMustHaveMod`: a mismatch refuses the connection), and the server's config wins (ServerSync's locked configuration).
- **Palworld** had 19 packages on Thunderstore on 2026-10-30. Its mods mostly come from elsewhere and use other loaders (UE4SS, `.pak` files) **[Unverified]**.

## 4. Design

### 4.1 Source, loader, mod set

- A **source** is where packages come from: `thunderstore` with a community id now; `manual` (an uploaded zip) and `workshop` later.
- A **loader** is how the game loads mods: `bepinex` first. A loader is code in Core, not an adapter hook. It knows its pack, the files that go next to the game's exe, the layout of a set folder, and the launch arguments it adds for *Mods on* and *Mods off*.
- A **mod set** is one exact list of packages, built into its own folder. An instance has an **active** set (used at the next start), a **previous** set (one step back) and the set it **runs** now; any other set is deleted.
- The adapter's `mods` section ([04 §3](04-adapter-spec.md#3-adapterjson)) becomes `{ "loader": "bepinex", "loaderPackage": "denikson-BepInExPack_Valheim", "sources": [{ "type": "thunderstore", "community": "valheim" }] }`, and the Valheim adapter becomes L3.

### 4.2 Data

Core keeps the mods state in SQLite, where `gamewarden-run` cannot change it (one migration in phase 1a):

| Table | Columns | Notes |
|---|---|---|
| `instance_mods` | `instance_id`, `enabled` (*Mods on*), `active_set_id`, `previous_set_id`, `running_set_id`, `share_code`, `share_code_created_at` | One row per instance with mods |
| `mod_sets` | `id`, `instance_id`, `created_at`, `origin` (`code`, `edit`, `update`, `rollback`), `state` (`building`, `ready`, `failed`), `job_id` | A set is never changed after it is `ready` |
| `mod_entries` | `set_id`, `package` (`Namespace-Name`), `version`, `enabled`, `origin` (`chosen`, `dependency`) | `chosen` = from a code or picked by the Owner |
| `package_versions` | `package`, `version`, `sha256`, `size`, `first_seen_at` | Global: one trusted hash per version for every instance |

`GET /instances/{id}/mods` returns the active set, the previous one, and a set that is building or failed with its job. `{paths.mods}\mods.json` is a copy of the active set for people and for manual backups.

### 4.3 Folders and launch

```text
instances\<id>\
  server\                SteamCMD's install, plus winhttp.dll and doorstop_config.ini (enabled = false)
  mods\
    sets\<set id>\
      BepInEx\core\      the loader pack's files
      BepInEx\plugins\<Namespace-Name>\
      BepInEx\patchers\<Namespace-Name>\
      BepInEx\config\    this set's config files (§4.7)
      loader\            the pack's winhttp.dll and doorstop_config.ini, the source for server\
      code-config\       the config files of the code this set came from, if any
    cache\               downloaded zips, <Namespace-Name-version>.zip
    mods.json            a copy of the active set
```

- **Launch.** When *Mods on* is set, the loader adds `--doorstop-enabled true --doorstop-target-assembly {paths.mods}\sets\<active>\BepInEx\core\BepInEx.Preloader.dll` in `LaunchSpecBuilder`; when it is off, `--doorstop-enabled false`. The `doorstop_config.ini` in `server\` says `enabled = false`, so a start without the arguments is vanilla too. **[Unverified: Doorstop 4 takes these arguments from `valheim_server.exe`'s command line, and BepInEx then uses the set's `BepInEx` folder as its root: spike S-M1, run before this design is accepted]**
- **Before each start** (in the instance's launch path, so crash restarts do it too): with *Mods on* and no ready active set, the start is refused with a message. Otherwise Core compares `server\winhttp.dll` and `doorstop_config.ini` with the active set's `loader\` copies by hash and copies them in as `gamewarden-run` when they differ or are missing, so a game update, a SteamCMD `validate` or a newer pack is handled. The active set then becomes the running set.
- **Logs.** BepInEx writes `sets\<active>\BepInEx\LogOutput.log`; phase 2 shows it as a second log in the Console tab (Runner and API change).
- **If S-M1 fails:** BepInEx goes into `server\` as the pack's README says, and only `plugins\`, `patchers\` and `config\` live in the set; activating a set then means copying them into `server\BepInEx\` while the server is stopped. That changes this section and §4.5, and ADR-0020 decision 1.

### 4.4 Getting a mod set

1. **Paste an r2modman code** (the main way). `code-preview` fetches the code and shows the set before anything changes: each mod with its version, *enabled* as the code has it, and its Thunderstore state (deprecated, not found). Missing dependencies are added (§4.5). Every mod goes to the server (§2, answer 5); mods disabled in the code stay in the set as disabled and are not installed. Core keeps the preview, with the code's `config/` files, for 30 minutes; *Apply* sends its id.
2. **Add a mod by name**: `Namespace-Name`, optionally with a version (the latest by default); its missing dependencies come along. Searching Thunderstore by name comes later: it needs the 14.5 MB community list cached on the VM.
3. **Change the set.** Remove a mod, enable or disable it, or pick another version. Each change makes a new set.

### 4.5 Sync job (`mods.sync`)

Resolve → download → verify → build `sets\<new>\` → make it active. The running server is not touched: the new set takes effect at the next start, and the panel shows *Restart to apply* with *Restart now*, as after a settings change ([11](11-web-panel.md)). So the job needs no stop and does not hold the instance lock. One mods job runs per instance at a time (409 `mods_busy` otherwise), and an instance is not purged while its mods job runs.

- **Resolve.** Versions that come from a code or that the Owner picked never change. A dependency that is missing is added at the highest version any mod in the set names. A dependency present at a lower version than a mod names is a warning in the preview, because r2modman installs newer versions of dependencies too **[Unverified: that mods treat dependency versions as minimums]**. The loader's pack is a dependency like any other.
- **Download.** From Thunderstore's download URL, with a `User-Agent` that names GameWarden and its repository, into a temporary folder only Core can write, then copied into `cache\` as `gamewarden-run`. The size must match Thunderstore's; the hash must match `package_versions`, or is recorded there on the first download (trust on first use: Thunderstore publishes no hash). Timeouts, 429 (with `Retry-After`) and 5xx are retried as SteamCMD is (`SteamCmdPolicy`: attempts and delays); a 404 fails the job with the package's name.
- **Build.** As `gamewarden-run` (`IInstanceFiles`, ADR-0009 decision 3), re-checking each zip's hash before it is extracted: the pack's `BepInEx\core` and its two Doorstop files into `loader\`; each mod by r2modman's install rules for BepInEx (`plugins` content into `plugins\<Namespace-Name>\`, `patchers` likewise, default `config` files into `config\`) **[Unverified: r2modman's rules for loose files at the zip root, spike S-M2]**; the code's config files into `code-config\`. Zip entries that leave their target folder or exceed the path length are refused, in packages and in codes alike.
- **Activate.** The new set becomes active and the old active set becomes previous, in one database write. Sets that are neither active, previous nor running are deleted, and `cache\` keeps only the zips those sets use.
- **Back one step.** *Back to the previous set* swaps the two pointers; nothing is rebuilt.

### 4.6 Sharing with friends

- **Share code for friends** (Mods tab): Core builds an r2modman zip from the active set (`export.r2x` with every enabled mod and its version), posts it to Thunderstore's profile API, stores the code with its time in `instance_mods`, and shows it with *Copy*. A friend pastes it in r2modman (*Import profile → From code*) and gets the same mods and versions. A new code is made on demand; old ones may expire.
- **Share text** ([07 §7](07-ux-and-user-flows.md#7-key-flows)) lists the mods and the last code, through `instance.connectInfo`, so friends with that permission get it too.
- **Configs in the code.** Off by default: mods such as Epic Loot and Norsemen send the server's settings to clients when they join, and other configs (a minimap's, a UI mod's) are personal. A switch adds the server's config files, after a note that anyone who has the code can read them.

### 4.7 Configs

- **Assembled at a set's first start**, while the server is stopped: the mods' default files from the build, then the config files of the set that ran before (so what the game or the Owner changed carries over), then `code-config\` (the code wins). Later starts of the same set leave its configs alone.
- A mod update never overwrites a config that exists.
- The Mods tab lists the files of the active set's `BepInEx\config\`, subfolders included, each viewable as text. Editing comes later (§2, answer 4).
- Until backups come back ([ADR-0018](../decisions/0018-backups-leave-the-mvp.md)), a manual backup copies the active set's `BepInEx\config\` along with `data\`.

### 4.8 Updates

- Never automatic (§2, answer 3). *Check for updates* asks Thunderstore for each package of the active set (one request per package, at most four at a time) and shows the newer versions; *Update* (one mod or all) makes a new set; *Back to the previous set* undoes it.
- With *Mods on*, *Update game* warns that large Valheim updates often break mods until they are updated **[Unverified: how often]**; turning mods off runs the server vanilla in the meantime.

### 4.9 Security

- Mods are .NET code. They run inside the game as `gamewarden-run` ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)), which may write only the instance folders ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)); a mod can still change another instance's files, as accepted in [06](06-security-and-access.md).
- Core never loads or runs mod files: it downloads and hashes them, and `gamewarden-run` extracts and copies them. The Doorstop files in `server\` are written by `gamewarden-run` and loaded only by the game, so ADR-0009 decision 2 holds.
- Changing mods, previews, update checks and share codes need `instance.mods.manage` (the Owner; Admins too once roles exist, [06 §5.2](06-security-and-access.md#52-roles)). A code is read only when someone with that permission pastes it, and nothing is installed before they apply the preview.
- Exact versions and one trusted hash per version; the Mods tab shows Thunderstore's *deprecated* flag and review status.

### 4.10 Panel

- **Mods tab** (adapters at L3): the *Mods on* switch; the active set as a table (name, version, newer version, enabled, *dependency*, Thunderstore state); *Import r2modman code*, *Add mod*, *Check for updates*, *Share code for friends*, *Back to the previous set*; the config files; the last mods job with its log; *Restart to apply* when the active set or *Mods on* differs from what runs.
- Help topics ([12](12-explanations.md)): *Mods on*, r2modman codes, why versions must match, config files.

### 4.11 API and CLI

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/instances/{id}/mods` | `instance.details.read` | Active, previous and building set; *Mods on*; newer versions last checked; the last share code |
| PUT | `/instances/{id}/mods` | `instance.mods.manage` | `{ entries }` or `{ previewId }` → 202 and a `mods.sync` job; 409 `mods_busy` |
| PUT | `/instances/{id}/mods/enabled` | `instance.mods.manage` | `{ enabled }`; applies at the next start |
| POST | `/instances/{id}/mods/code-preview` | `instance.mods.manage` | `{ code }` → `{ previewId, entries, warnings, configFiles }`; changes nothing |
| POST | `/instances/{id}/mods/share-code` | `instance.mods.manage` | `{ includeConfigs }` → `{ code, createdAt }` |
| POST | `/instances/{id}/mods/check-updates` | `instance.mods.manage` | Newer versions from Thunderstore |
| POST | `/instances/{id}/mods/rollback` | `instance.mods.manage` | Swap active and previous |
| GET | `/instances/{id}/mods/configs`, `/configs/content?path=` | `instance.details.read` | Config files and their text |

After the API change: rebuild Core, `npm run gen:api`, commit both ([CLAUDE.md](../CLAUDE.md)).

CLI: `gamewarden mods <id>`, `mods import <id> <code>`, `mods add <id> <Namespace-Name>[@version]`, `mods remove`, `mods update [--all]`, `mods share <id>`, `mods on|off <id>`, `mods rollback <id>`.

### 4.12 Tests and the Linux dev box

- Thunderstore is reached through one client class with a fake `HttpMessageHandler` in tests (recorded answers for Epic Loot, Jötunn, BepInExPack and a profile code).
- The BepInEx loader's layout rules (which zip entry goes where) are pure functions tested in `Unit`, with paths compared case-sensitively as on Linux.
- FakeGame gets a fake loader, so `Integration` tests run the whole job, the activation and the launch arguments on Linux ([ADR-0016](../decisions/0016-windows-as-an-integration-layer.md)). Doorstop itself is checked on the VM.

### 4.13 Other games

Palworld (next, [ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)) adds a `manual` source (an uploaded zip) and a second loader **[Unverified: UE4SS or `.pak` files]**. The sets, the job, the activation and the Mods tab stay the same; r2modman codes stay a Thunderstore feature. A `workshop` source (tModLoader and others) comes later.

## 5. Spikes

| Id | Where | Question | Done when |
|---|---|---|---|
| S-M1 | A `windows-2025` GitHub runner (a spike workflow, no VM change), before this design is accepted | Does `valheim_server.exe` load BepInEx 5.4.2360 from a folder outside its own through the Doorstop arguments, and stay vanilla with `--doorstop-enabled false`? | `LogOutput.log` in the outside folder shows the chainloader and Epic Loot loading; with `false` nothing loads |
| S-M2 | Dev box | r2modman's install rules for BepInEx packages (read r2modmanPlus), checked on the Epic Loot, Jötunn and Norsemen zips | The build step places their files as r2modman does |
| S-M3 | Dev box and a PC | A code made by GameWarden imports in r2modman; a code made by r2modman imports in GameWarden | Both ways work with the group's real code |

## 6. Plan

| Phase | Content | Checks |
|---|---|---|
| 0 | This document and [ADR-0020](../decisions/0020-mods-in-set-folders-as-pinned-sets.md), with S-M1's result; the Owner approves | Docs check; S-M1 |
| 1a | Data model and migration, Thunderstore client (faked in tests), resolver, `GET` / `PUT /mods`, `mods.sync` with the fake loader, activation and rollback | `Unit` and `Integration` on Linux; Windows CI |
| 1b | BepInEx loader, Doorstop files, *Mods on*, Valheim adapter at L3; spec updates in 03, 04, 05, 06, 07, 08 §1 and 10 §8 | Release candidate; on the VM: BepInEx and Epic Loot added by name load |
| 1c | Code preview and import, share code (S-M3), configs, CLI | Release candidate; on the VM: the group's code |
| 2 | Panel: Mods tab, import, add, update check, rollback, share code, configs, BepInEx log; help topics | Panel tests and screenshots; VM walk-through: Epic Loot and Norsemen, a friend joins with the share code |
| 3 | Fixes from the walk-through; then Palworld | — |

## 7. Questions for the Owner

1. Configs in the share code off by default (§4.6)? Proposed: yes.
2. Anything the group does in r2modman that this misses (several profiles per server, mods outside Thunderstore)?
3. Searching Thunderstore by name in the panel can wait (§4.4)? Proposed: yes; adding by name and importing codes come first.
