# ADR-0020: Mods live in a profile folder outside the game install, as a pinned set Core owns, exchanged as r2modman codes

- **Status:** Proposed
- **Date:** 2026-10-30

## Context

- Mods for Valheim come next ([ADR-0019](0019-discord-joins-the-backlog-mods-next.md)). The group installs mods with r2modman, and its mods (Epic Loot, Norsemen) refuse players whose versions differ from the server's ([13 §3](../docs/13-mods.md#3-facts-checked-2026-10-30)).
- [04 §7](../docs/04-adapter-spec.md#7-mods) sketched a Thunderstore provider that installs "per `loader` rules" and keeps the list in `{paths.mods}\mods.json`, a file `gamewarden-run` can write ([ADR-0009](0009-data-root-acl-and-secrets.md)). It did not say where the loader goes, how versions are pinned, or how players get the same set.
- BepInEx is loaded by Unity Doorstop. r2modman keeps BepInEx and the mods in a profile folder and points Doorstop at it with launch arguments, so the game folder stays unchanged.
- Thunderstore publishes exact dependency versions but no file hashes. r2modman profile codes carry a mod list with exact versions and the configs.
- The Owner's answers of 2026-10-30 ([13 §2](../docs/13-mods.md#2-the-owners-answers-2026-10-30)): r2modman codes both ways, no automatic updates, every mod of a code goes to the server, configs viewable first and editable later.

## Decision

1. **Profile folder.** An instance's mods live in `instances\<id>\mods\profile\` with the loader (BepInEx), next to `staging\` (the next set) and `previous\` (one step back). The game folder gets only the loader's entry files (Doorstop's `winhttp.dll` and a `doorstop_config.ini` that is off). A bool setting *Mods on* passes the loader's launch arguments, on and off (`offArgs`). If spike S-M1 shows that `valheim_server.exe` cannot load BepInEx this way, BepInEx goes into the game folder and the rest of this decision stays.
2. **A pinned set that Core owns.** The mod set (package, exact version, enabled, added by the Owner or as a dependency, SHA-256, size) is kept in SQLite. `mods.json` in the instance folder is a copy. The first download of a version records its hash; later downloads must match.
3. **Changes go through one job.** `mods.sync` resolves, downloads, verifies, builds `staging\` and swaps it in while the server is stopped. Configs carry over unchanged. Nothing updates by itself.
4. **r2modman codes are the exchange format** with players: imported to set a server's mods, and made from the server's set for friends.
5. **Sources and loaders are separate.** A loader (BepInEx first) is code in Core; a source (Thunderstore first) supplies packages. Palworld will add a source and a loader without changing the set, the job or the panel.

## Consequences

- SteamCMD updates and file checks leave the mods alone. *Mods on / off* and one step back are cheap.
- A mod cannot rewrite the list Core installs from.
- Core depends on two Thunderstore endpoints that are not formally documented as stable: the package API and `legacyprofile`. If they change, importing and sharing codes stop working, but installed servers keep running.
- Trust on first use: a package that was already bad at its first download is not caught. Only the Owner adds mods.
- [04 §3 and §7](../docs/04-adapter-spec.md#7-mods), [03 §5](../docs/03-domain-model.md#5-jobs), [05](../docs/05-api.md) and [07](../docs/07-ux-and-user-flows.md) change with phase 1 ([13 §6](../docs/13-mods.md#6-plan)).

## Alternatives considered

- **Install BepInEx into the game folder** (the pack's README): simpler. But it mixes mods into SteamCMD's files, makes *off* mean deleting files, and leaves no clean step back. It stays the fallback if S-M1 fails.
- **`mods.json` as the source of truth** (04 §7 as sketched): a file the game's user can write, so a mod could change what Core installs next.
- **Always the latest version:** breaks the exact-version match the group's mods need, and updates mods behind the Owner's back.
- **Only Thunderstore search, no codes:** the friends would rebuild the list by hand in r2modman.
