# 13 · Mods: Valheim first, shared with r2modman codes

**Status: Accepted (the Owner approved it on 2026-10-30 with the answers in §2).** The Owner put mods next ([ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)). Its decision is [ADR-0020](../decisions/0020-mods-in-set-folders-as-pinned-sets.md). Adapter spec: [04 §7](04-adapter-spec.md#7-mods); run-as user and folder rights: [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md), [ADR-0009](../decisions/0009-data-root-acl-and-secrets.md).

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
| 6 | Configs in the share code? | No: the share code never carries config files |
| 7 | Anything the group does in r2modman that this misses? | No: the friends use r2modman to install and update mods quickly, so GameWarden follows r2modman's formats |
| 8 | Search Thunderstore in the panel now or later? | Now, if it can be made stable (phase 2, §4.4) |

## 3. Facts (checked 2026-10-30)

- **BepInEx for Valheim.** `denikson-BepInExPack_Valheim` 5.4.2360, published 2026-10-28 (BepInEx 5.4.23.6, Unity Doorstop 4.6.0), pinned on Thunderstore. In its zip the files sit under `BepInExPack_Valheim/`: `winhttp.dll`, `doorstop_config.ini`, `BepInEx/core/…`, `BepInEx/config/BepInEx.cfg`. On Windows, `winhttp.dll` next to the game's exe is a proxy DLL (Doorstop) that loads BepInEx; the pack's README says to just run the game. Sources: [the package's API entry](https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/) and its zip.
- **How r2modman starts a modded game.** It keeps BepInEx and the mods in a profile folder and passes Doorstop arguments: `--doorstop-enabled true --doorstop-target-assembly <profile>\BepInEx\core\BepInEx.Preloader.dll` (Doorstop 4), and `--doorstop-enabled false` for vanilla ([r2modmanPlus `BepInExGameInstructions.ts`](https://github.com/ebkr/r2modmanPlus/blob/develop/src/r2mm/launching/instructions/instructions/loader/BepInExGameInstructions.ts)). Spike S-M1 confirmed it for `valheim_server.exe` on 2026-10-30 (§5).
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

- **Launch.** When *Mods on* is set, the loader adds `--doorstop-enabled true --doorstop-target-assembly {paths.mods}\sets\<active>\BepInEx\core\BepInEx.Preloader.dll` in `LaunchSpecBuilder`; when it is off, `--doorstop-enabled false` once the entry files are in `server\` (a server that never had mods gets no extra arguments). The `doorstop_config.ini` in `server\` says `enabled = false`, so a start without the arguments is vanilla too. Spike S-M1 showed all three on 2026-10-30 (§5); the VM checks it again as `gamewarden-run` under the service (phase 1b).
- **Before each start** (in the instance's launch path, so crash restarts do it too): with *Mods on* and no ready active set, the start is refused with a message. Otherwise Core compares `server\winhttp.dll` and `doorstop_config.ini` with the active set's `loader\` copies by hash and copies them in as `gamewarden-run` when they differ or are missing, so a game update, a SteamCMD `validate` or a newer pack is handled. The active set then becomes the running set.
- **Logs.** BepInEx writes `sets\<active>\BepInEx\LogOutput.log`; phase 2 shows it as a second log in the Console tab (Runner and API change).

### 4.4 Getting a mod set

1. **Paste an r2modman code** (the main way). `code-preview` fetches the code and shows the set before anything changes: each mod with its version, *enabled* as the code has it, and its Thunderstore state (deprecated, not found). Missing dependencies are added (§4.5). Every mod goes to the server (§2, answer 5); mods disabled in the code stay in the set as disabled and are not installed. Core keeps the preview, with the code's `config/` files, for 30 minutes; *Apply* sends its id. Besides `config/`, r2modman puts the profile's other config-like files (`.cfg`, `.txt`, `.json`, `.yml`, `.yaml`, `.ini` anywhere in the profile) into the code by their path; GameWarden takes only `config/` and lists the others in the preview's warnings, and leaves out programs and scripts as r2modman's import does (phase 1c).
2. **Add a mod by name**: `Namespace-Name`, optionally with a version (the latest by default); its missing dependencies come along.
3. **Search Thunderstore** (phase 2, §2 answer 8). Core downloads the community list (14.5 MB compressed) into a cache file when someone searches and the cache is more than a day old, keeps a small index of it (package, owner, latest version, description, categories, deprecated) and searches it in memory. Only one download runs at a time; a failed download keeps the old cache, and adding by exact name works without it.
4. **Change the set.** Remove a mod, enable or disable it, or pick another version. Each change makes a new set.

### 4.5 Sync job (`mods.sync`)

Resolve → download → verify → build `sets\<new>\` → make it active. The running server is not touched: the new set takes effect at the next start, and the panel shows *Restart to apply* with *Restart now*, as after a settings change ([11](11-web-panel.md)). So the job needs no stop and does not hold the instance lock. One mods job runs per instance at a time (409 `mods_busy` otherwise), and an instance is not purged while its mods job runs.

- **Resolve.** Versions that come from a code or that the Owner picked never change. A dependency that is missing is added at the highest version any mod in the set names. A dependency present at a lower version than a mod names is a warning in the preview, because r2modman installs newer versions of dependencies too **[Unverified: that mods treat dependency versions as minimums]**. The loader's pack is a dependency like any other.
- **Download.** From Thunderstore's download URL, with a `User-Agent` that names GameWarden and its repository, into a temporary folder only Core can write, then copied into `cache\` as `gamewarden-run`. The size must match Thunderstore's; the hash must match `package_versions`, or is recorded there on the first download (trust on first use: Thunderstore publishes no hash). Timeouts, 429 (with `Retry-After`) and 5xx are retried as SteamCMD is (`SteamCmdPolicy`: attempts and delays); a 404 fails the job with the package's name.
- **Build.** As `gamewarden-run` (`IInstanceFiles`, ADR-0009 decision 3), re-checking each zip's hash before it is extracted: the pack's `BepInEx\core` and its two Doorstop files into `loader\`; each mod by r2modman's install rules for BepInEx (`plugins` content into `plugins\<Namespace-Name>\`, `patchers` likewise, default `config` files into `config\`) **[Unverified: r2modman's rules for loose files at the zip root, spike S-M2]**; the code's config files into `code-config\`. Zip entries that leave their target folder or exceed the path length are refused, in packages and in codes alike.
- **Activate.** The new set becomes active and the old active set becomes previous, in one database write. Sets that are neither active, previous nor running are deleted, and `cache\` keeps only the zips those sets use.
- **Back one step.** *Back to the previous set* swaps the two pointers; nothing is rebuilt.

### 4.6 Sharing with friends

- **Share code for friends** (Mods tab): Core builds an r2modman zip from the active set (`export.r2x` with every enabled mod and its version), posts it to Thunderstore's profile API, stores the code with its time in `instance_mods`, and shows it with *Copy*. A friend pastes it in r2modman (*Import profile → From code*) and gets the same mods and versions. A new code is made on demand; old ones may expire. Thunderstore makes at most six codes a minute per address (its `modpacks` source, 2026-10-30); Core tries again after a 429 and then says to wait a minute.
- **Share text** ([07 §7](07-ux-and-user-flows.md#7-key-flows)) lists the mods and the last code, through `instance.connectInfo`, so friends with that permission get it too.
- **No configs in the code** (§2, answer 6): mods such as Epic Loot and Norsemen send the server's settings to clients when they join, and other configs (a minimap's, a UI mod's) are personal.

### 4.7 Configs

Changed by [ADR-0021](../decisions/0021-mod-configs-edited-with-revisions.md) on 2026-10-31, at the Owner's request:

- **The active set's `BepInEx\config\` is the instance's config.** A new set's config folder is assembled when it is **built**: the mods' default files, then the current active set's config files (so what the game or the Owner changed carries over), then the code's files (the code wins). Activation makes it the config the panel shows and edits. Until 2026-10-31 this happened at a set's first start, with the code's files kept in `code-config\`.
- A mod update never overwrites a config that exists.
- **Editing** (the panel's text editor; a form for BepInEx `.cfg` files may come later): text files (`.cfg`, `.json`, `.yml`, `.yaml`, `.txt`, `.ini`) up to 1 MB. *Save* sends the text and the SHA-256 it started from; a file that changed on disk since answers 409 `config_changed`. JSON is checked first. The file is written at once as `gamewarden-run`; when the active set is the running one, mods that watch their file reload it, others need a restart, and the panel says so.
- **Revisions**: each save stores the new text (`edit`, `restore`); before it, a file on disk that differs from the newest revision is stored as `server` (what the game, a mod update or a code wrote). The newest 50 per file are kept, per instance and path, so they survive set changes. The panel lists them, compares one with the current file line by line, and restores one (a new revision).
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

- **Mods tab** (adapters at L3, built in phase 2 on 2026-10-30, [11 §4](11-web-panel.md#4-screens)): the *Mods on* switch; the active set as a table (name, version, newer version, enabled, *dependency*, Thunderstore state); *Import r2modman code*, *Add mod* (search, or an exact name), *Check for updates* and *Update all*, *Share code for friends*, *Back to the previous set*; the config files; the building or failed set with its job's log; *Restart to apply* when the active set or *Mods on* differs from what runs (Core's `restartRequired`, from the running set and how the last start went).
- As built: Thunderstore's state of each package comes with an update check (`states`), so the table says *Not checked* until one ran; the review status is not shown. The BepInEx log is a section of the Mods tab (`GET /mods/log`), not a second log in the Console tab as §4.3 first planned. Search ranks an exact name, then name prefixes, names that contain the text, owners and descriptions, and within each puts deprecated packages last (on 2026-10-30, 5,867 of Valheim's 14,117 packages were deprecated).
- Help topics ([12](12-explanations.md)): *Mods on*, r2modman codes, why versions must match, config files.

### 4.11 API and CLI

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/instances/{id}/mods` | `instance.details.read` | Active, previous and building set; *Mods on*; newer versions last checked; the last share code |
| PUT | `/instances/{id}/mods` | `instance.mods.manage` | `{ entries }` or `{ previewId }` → 202 and a `mods.sync` job; 409 `mods_busy` |
| PUT | `/instances/{id}/mods/enabled` | `instance.mods.manage` | `{ enabled }`; applies at the next start |
| POST | `/instances/{id}/mods/code-preview` | `instance.mods.manage` | `{ code }` → `{ previewId, entries, warnings, configFiles }`; changes nothing |
| POST | `/instances/{id}/mods/share-code` | `instance.mods.manage` | → `{ code, createdAt }` |
| GET | `/instances/{id}/mods/search?q=` | `instance.mods.manage` | Search the cached community list of the instance's source (phase 2) |
| POST | `/instances/{id}/mods/check-updates` | `instance.mods.manage` | Newer versions from Thunderstore |
| POST | `/instances/{id}/mods/rollback` | `instance.mods.manage` | Swap active and previous |
| GET | `/instances/{id}/mods/configs`, `/configs/content?path=` | `instance.details.read` | Config files and their text (with its SHA-256) |
| PUT | `/instances/{id}/mods/configs/content?path=` | `instance.mods.manage` | `{ content, baseSha256 }` → the new SHA-256 and revision; 409 `config_changed` ([ADR-0021](../decisions/0021-mod-configs-edited-with-revisions.md)) |
| GET | `/instances/{id}/mods/configs/revisions?path=`, `/revisions/{revisionId}` | `instance.details.read` | A file's revisions, and one revision's text |
| POST | `/instances/{id}/mods/configs/revisions/{revisionId}/restore` | `instance.mods.manage` | `{ baseSha256 }` → writes that text as a new revision |

After the API change: rebuild Core, `npm run gen:api`, commit both ([CLAUDE.md](../CLAUDE.md)).

CLI: `gamewarden mods <id>`, `mods import <id> <code>`, `mods add <id> <Namespace-Name>[@version]`, `mods remove`, `mods update [--all]`, `mods share <id>`, `mods on|off <id>`, `mods rollback <id>`, `mods configs <id> [<path>]`.

### 4.12 Tests and the Linux dev box

- Thunderstore is reached through one client class with a fake `HttpMessageHandler` in tests (recorded answers for Epic Loot, Jötunn, BepInExPack and a profile code).
- The BepInEx loader's layout rules (which zip entry goes where) are pure functions tested in `Unit`, with paths compared case-sensitively as on Linux.
- FakeGame gets a fake loader, so `Integration` tests run the whole job, the activation and the launch arguments on Linux ([ADR-0016](../decisions/0016-windows-as-an-integration-layer.md)). Doorstop itself is checked on the VM.

### 4.13 Other games

Palworld (next, [ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)) adds a `manual` source (an uploaded zip) and a second loader **[Unverified: UE4SS or `.pak` files]**. The sets, the job, the activation and the Mods tab stay the same; r2modman codes stay a Thunderstore feature. A `workshop` source (tModLoader and others) comes later.

## 5. Spikes

| Id | Where | Question | Done when | Result |
|---|---|---|---|---|
| S-M1 | A `windows-2025` GitHub runner ([spike](../spikes/s-m1-doorstop/README.md), no VM change) | Does `valheim_server.exe` load BepInEx 5.4.2360 from a folder outside its own through the Doorstop arguments, and stay vanilla with `--doorstop-enabled false`? | `LogOutput.log` in the outside folder shows the chainloader and Epic Loot loading; with `false` nothing loads | **Passed 2026-10-30:** with the arguments, BepInEx 5.4.2360 ran from `mods\sets\1\` under Unity 6000.0.75 and loaded Jötunn 2.30.2 and Epic Loot 0.15.4, with no log in `server\BepInEx\`; off and without arguments, nothing loaded and the server ran |
| S-M2 | Dev box | r2modman's install rules for BepInEx packages (read r2modmanPlus), checked on the Epic Loot, Jötunn and Norsemen zips | The build step places their files as r2modman does | **Done 2026-10-30** (r2modmanPlus `develop` at `b09ec5a`): the pack goes by `BepInExInstaller.ts` (the content of its root folder, `BepInExPack_Valheim`, from `modloaderPackages` in `ecosystem.json`); other packages by `InstallRulePluginInstaller.ts` with Valheim's `installRules` in `ecosystem.json`. A folder named `plugins`, `patchers`, `monomod`, `core` or `SlimVML` (any case, the outermost first) goes into `BepInEx\<folder>\<Namespace-Name>\` with its subfolders, `config` into `BepInEx\config\`; any other file goes by extension, `.mm.dll` into `monomod` and everything else into `plugins\<Namespace-Name>\`, flattened to its name. So loose files at the zip root, Thunderstore's `manifest.json`, `icon.png` and readme included, land in the plugin folder. Checked on the entry names of BepInExPack_Valheim 5.4.2360, Epic Loot 0.15.4, Jötunn 2.30.2 (its entries use `\`), JsonDotNET 13.0.4 and VikingNPC_Continued 0.4.2 (Norsemen, its DLL at the root) in `BepInExLoaderTests`. GameWarden leaves out what r2modman would also copy from the pack (Linux and macOS scripts, `doorstop_libs\`, changelogs) |
| S-M3 | Dev box and a PC | A code made by GameWarden imports in r2modman; a code made by r2modman imports in GameWarden | Both ways work with the group's real code | **Dev box part done 2026-10-30** (phase 1c, r2modmanPlus `develop`): the format was read from `ProfileModList.createExport` and `exportModListAsCode` (writing), `ProfileImportExport.downloadProfileCode` and `ProfileUtils.parseYamlToExportFormat` and `extractConfigsToImportedProfile` (reading), and Thunderstore's `modpacks` API (a code is a UUID; six new codes a minute). `R2ModmanCodeTests` read a code built as r2modman writes it (`export.r2x` as js-yaml dumps it, `config/` with subfolders and `\` separators, files outside `config/`, scripts) and refuse codes that are not r2modman's, over the limits or with entries that leave their folder; the share code's `export.r2x` matches js-yaml's output byte for byte and holds no `config/`. The API tests cover the preview's states and expiry, the import (versions and *enabled* as in the code, configs into `code-config\`), the config order at a set's first start, the share code as posted, 404 and 429, the update check and the config views; the CLI tests run `import`, `add`, `update`, `remove` and `share`. **Left for the VM** ([10 §8.5](10-build-test-release.md#85-mods-phase-1b), steps 11–18): the group's real code into GameWarden, and a GameWarden code into r2modman on a friend's PC |

## 6. Plan

| Phase | Content | Checks |
|---|---|---|
| 0 | This document and [ADR-0020](../decisions/0020-mods-in-set-folders-as-pinned-sets.md), with S-M1's result (passed); the Owner approves | Docs check; S-M1 |
| 1a | Data model and migration, Thunderstore client (faked in tests), resolver, `GET` / `PUT /mods`, `mods.sync` with the fake loader, activation and rollback | `Unit` and `Integration` on Linux; Windows CI |
| 1b | BepInEx loader, Doorstop files, *Mods on*, Valheim adapter at L3; spec updates in 03, 04, 05, 06, 07, 08 §1 and 10 §8 | Release candidate; on the VM: BepInEx and Epic Loot added by name load |
| 1c | Code preview and import, share code (S-M3), configs, CLI | Release candidate; on the VM: the group's code |
| 2 | Panel: Mods tab, import, add, Thunderstore search, update check, rollback, share code, configs, BepInEx log; help topics | Panel tests and screenshots; VM walk-through: Epic Loot and Norsemen, a friend joins with the share code |
| 3 | Config files edited in the panel, with revisions; configs carried over at build ([ADR-0021](../decisions/0021-mod-configs-edited-with-revisions.md)). The VM walk-through passed with `v0.2.0-rc.9` on 2026-10-31 | Unit and API tests; panel tests and screenshots; on the VM: edit a config while the server runs, restore an older revision |
| 4 | Palworld ([ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)) | — |

## 7. Questions

None open: the Owner answered them on 2026-10-30 (§2).
