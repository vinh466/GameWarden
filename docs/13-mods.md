# 13 · Mods: Valheim first, shared with r2modman codes

**Status: Proposed (2026-10-30).** The Owner put mods next ([ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)) and answered the first questions on 2026-10-30 (§2). The decision it asks for is [ADR-0020](../decisions/0020-mods-in-a-profile-folder-with-a-pinned-set.md). Adapter spec: [04 §7](04-adapter-spec.md#7-mods); run-as user and folder rights: [ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md), [ADR-0009](../decisions/0009-data-root-acl-and-secrets.md).

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
- A **loader** is how the game loads mods: `bepinex` first. A loader is code in Core, not an adapter hook: it knows its pack, the files that go next to the game's exe, the profile layout and the launch arguments for on and off.
- The **mod set** of an instance is the exact list: for each package `Namespace-Name`, its version, *enabled*, *added by the Owner* or *dependency*, its SHA-256 and size. Core keeps it in SQLite, where `gamewarden-run` cannot change it; `{paths.mods}\mods.json` is a copy for people and for manual backups.
- The adapter's `mods` section ([04 §3](04-adapter-spec.md#3-adapterjson)) becomes `{ "loader": "bepinex", "loaderPackage": "denikson-BepInExPack_Valheim", "sources": [{ "type": "thunderstore", "community": "valheim" }] }`, and the Valheim adapter becomes L3.

### 4.2 Folders and launch

```text
instances\<id>\
  server\        SteamCMD's install, plus winhttp.dll and doorstop_config.ini (enabled = false)
  mods\
    profile\     the active set: BepInEx\core, plugins\<Namespace-Name>\, patchers\, config\
    staging\     the next set while a sync runs
    previous\    the set before the last sync (one step back)
    cache\       downloaded zips, <Namespace-Name-version>.zip
    mods.json    a copy of the set
```

- **On / off.** A bool setting *Mods on* with an `arg` target ([04 §5.2](04-adapter-spec.md#52-targets)), the mechanism `public` uses: on passes `--doorstop-enabled true --doorstop-target-assembly {paths.mods}\profile\BepInEx\core\BepInEx.Preloader.dll`, off passes `--doorstop-enabled false` (`offArgs`). The `doorstop_config.ini` that GameWarden places says `enabled = false`, so a start without the arguments is vanilla too. **[Unverified: Doorstop 4 takes these arguments from `valheim_server.exe`'s command line, and BepInEx then uses the profile's `BepInEx` folder as its root: spike S-M1]**
- **Game updates.** Before each start with mods on, Core puts the two Doorstop files back if they are missing, so a game update or a SteamCMD `validate` cannot leave the server without them.
- **If S-M1 fails:** BepInEx goes into `server\` as the pack's README says, and the profile holds only `plugins\`, `patchers\` and `config\`, copied in on each sync. Everything else in this document stays.
- **Logs.** BepInEx writes `profile\BepInEx\LogOutput.log`; the server's Console tab offers it as a second log.

### 4.3 Getting a mod set

1. **Paste an r2modman code** (the main way). Core fetches the code and shows the set before changing anything: each mod with its version, *enabled* as the code has it, and its Thunderstore state (deprecated, not found). Missing dependencies are added. Every mod goes to the server (§2, answer 5); mods disabled in the code stay in the set as disabled and are not installed. *Apply* starts a sync. The code's `config/` files are copied into the profile's `BepInEx\config\`, replacing files of the same name and keeping the others.
2. **Add from Thunderstore.** Search the community list by name; Core caches the list and refreshes it at most once a day, when someone searches. Pick a version (the latest by default); its dependencies come along.
3. **Change the set.** Remove a mod, enable or disable it, or pick another version.

### 4.4 Sync job (`mods.sync`)

Resolve → download → verify → build `staging\` → stop → swap → start again if it was running. The job holds the instance lock ([03 §2](03-domain-model.md#2-instance-states)); a failure before the swap leaves the server as it was.

- **Resolve.** The Owner's mods plus their dependencies, the loader's pack included. When two mods need different versions of one dependency, the highest wins and the job log says so **[Unverified: that r2modman does the same]**.
- **Download.** From Thunderstore's download URL into `cache\`, with a `User-Agent` that names GameWarden and its repository. The size must match the listing. The first download of a version stores its SHA-256 in the set; every later download of that version must match it. Thunderstore publishes no hash, so this is trust on first use.
- **Build.** Extract as `gamewarden-run` (`IInstanceFiles`, ADR-0009 §3) into `staging\` by r2modman's install rules for BepInEx: the pack's `BepInEx\core`; each mod's `plugins` content into `plugins\<Namespace-Name>\`, its `patchers` into `patchers\<Namespace-Name>\`, its `config` only where no file of that name exists. Zip entries that leave the target folder or exceed the path length are refused. **[Unverified: r2modman's exact rules for loose files at the zip root, spike S-M2]**
- **Swap.** With the server stopped: `profile\` → `previous\`, `staging\` → `profile\`; the old profile's `config\` moves into the new one unchanged.
- **Back one step.** *Back to the previous set* swaps `previous\` in again.

### 4.5 Sharing with friends

- **Share code for friends** (Mods tab and the Share text): Core builds an r2modman zip from the set (`export.r2x` with every enabled mod and its version), posts it to Thunderstore's profile API and shows the code with *Copy* and the time it was made. A friend pastes it in r2modman (*Import profile → From code*) and gets the same mods and versions. A new code is made each time; old ones may expire.
- **Configs in the code.** Off by default: Valheim mods such as Epic Loot and Norsemen send the server's settings to clients when they join, and other configs (a minimap's, a UI mod's) are personal. A switch adds the server's config files, after a note that anyone who has the code can read them.

### 4.6 Updates

- Never automatic (§2, answer 3). *Check for updates* compares each mod with Thunderstore's latest version and shows the newer ones; *Update* (one mod or all) makes a new set and starts a sync; *Back to the previous set* undoes it.
- With mods on, *Update game* warns that large Valheim updates often break mods until they are updated **[Unverified: how often]**; turning mods off runs the server vanilla in the meantime.

### 4.7 Configs

The Mods tab lists the files in the profile's `BepInEx\config\`, each viewable as text. A mod update never overwrites them. Editing comes later (§2, answer 4). Until backups come back ([ADR-0018](../decisions/0018-backups-leave-the-mvp.md)), a manual backup copies `mods\profile\BepInEx\config\` along with `data\`.

### 4.8 Security

- Mods are .NET code. They run inside the game as `gamewarden-run` ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)), which may write only the instance folders ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md)); a mod can still change another instance's files, as accepted in [06](06-security-and-access.md).
- Core never loads or runs mod files: it downloads them, hashes them and has `gamewarden-run` extract them.
- Only the Owner changes mods (`instance.mods.manage`, [06](06-security-and-access.md)). A code is read only when the Owner pastes it, and nothing is installed before the Owner confirms the list.
- Exact versions and stored hashes; the Mods tab shows Thunderstore's *deprecated* flag and review status.

### 4.9 Panel

- **Mods tab** (adapters at L3): the *Mods on* switch; the set as a table (name, version, newer version, enabled, *dependency*, Thunderstore state); *Import r2modman code*, *Add from Thunderstore*, *Check for updates*, *Share code for friends*, *Back to the previous set*; the config files; the last sync job with its log.
- Help topics ([12](12-explanations.md)): *Mods on*, r2modman codes, why versions must match, config files.

### 4.10 API and CLI

| Method | Path | Notes |
|---|---|---|
| GET | `/instances/{id}/mods` | The set, *Mods on*, the newer versions last checked, the last sync |
| PUT | `/instances/{id}/mods` | The whole wanted set → 202 and a `mods.sync` job |
| POST | `/instances/{id}/mods/code-preview` | `{ code }` → the set the code would give; changes nothing |
| POST | `/instances/{id}/mods/share-code` | `{ includeConfigs }` → `{ code, createdAt }` |
| POST | `/instances/{id}/mods/check-updates` | Newer versions from Thunderstore |
| POST | `/instances/{id}/mods/rollback` | Swap the previous set back → job |
| GET | `/instances/{id}/mods/configs`, `/configs/{file}` | Config file list and text |
| GET | `/adapters/{id}/mods/search?q=` | Search the cached community list |

CLI: `gamewarden mods <id>`, `mods import <id> <code>`, `mods add <id> <Namespace-Name>[@version]`, `mods remove`, `mods update [--all]`, `mods share <id>`, `mods on|off <id>`, `mods rollback <id>`.

### 4.11 Other games

Palworld (next, [ADR-0019](../decisions/0019-discord-joins-the-backlog-mods-next.md)) adds a `manual` source (an uploaded zip) and a second loader **[Unverified: UE4SS or `.pak` files]**. The set, the sync job, the profile / staging / previous swap and the Mods tab stay the same; r2modman codes stay a Thunderstore feature. A `workshop` source (tModLoader and others) comes later.

## 5. Spikes

| Id | Where | Question | Done when |
|---|---|---|---|
| S-M1 | VM, first release candidate of phase 1 | Does `valheim_server.exe` load BepInEx 5.4.2360 from `mods\profile` through the Doorstop arguments, and run vanilla with `--doorstop-enabled false`? | `LogOutput.log` lists Epic Loot; a friend with the same r2modman code joins; off gives a vanilla server |
| S-M2 | Dev box | r2modman's install rules for BepInEx packages (read r2modmanPlus), checked on the Epic Loot, Jötunn and Norsemen zips | The build step places their files as r2modman does |
| S-M3 | Dev box and a PC | A code made by GameWarden imports in r2modman; a code made by r2modman imports in GameWarden | Both ways work with the group's real code |

## 6. Plan

| Phase | Content | Checks |
|---|---|---|
| 0 | This document and [ADR-0020](../decisions/0020-mods-in-a-profile-folder-with-a-pinned-set.md); the Owner approves | Docs check |
| 1 | Core: Thunderstore client (cached list, package metadata, download), resolver, the set in SQLite and `mods.json`, `mods.sync` with staging and swap, the BepInEx loader and *Mods on*, code preview and import, share code, CLI; a fake loader for tests; Valheim adapter at L3 | Unit and Integration on Linux; Windows CI; release candidate → S-M1 on the VM with the group's code |
| 2 | Panel: Mods tab, import, add, update check, rollback, share code, config view; help topics | Panel tests and screenshots; VM walk-through: Epic Loot and Norsemen, a friend joins with the share code |
| 3 | Fixes from the walk-through; then Palworld | — |

## 7. Questions for the Owner

1. Configs in the share code off by default (§4.5)? Proposed: yes.
2. Anything the group does in r2modman that this misses (several profiles per server, mods outside Thunderstore)?
