# ADR-0020: Mods live in set folders outside the game install, as pinned sets Core owns, exchanged as r2modman codes

- **Status:** Proposed
- **Date:** 2026-10-30

## Context

- Mods for Valheim come next ([ADR-0019](0019-discord-joins-the-backlog-mods-next.md)). The group installs mods with r2modman. Its mods (Epic Loot, Norsemen) refuse players whose versions differ from the server's ([13 §3](../docs/13-mods.md#3-facts-checked-2026-10-30)).
- [04 §7](../docs/04-adapter-spec.md#7-mods) sketched a Thunderstore provider that installs "per `loader` rules" and keeps the list in `{paths.mods}\mods.json`. `gamewarden-run` can write that file ([ADR-0009](0009-data-root-acl-and-secrets.md)). The sketch did not say where the loader goes, how versions are pinned, or how players get the same set.
- Unity Doorstop loads BepInEx. r2modman keeps BepInEx and the mods in a profile folder and points Doorstop at it with launch arguments, so the game folder stays unchanged.
- Thunderstore publishes exact dependency versions, but no file hashes. An r2modman profile code carries a mod list with exact versions, plus the configs.
- The Owner answered on 2026-10-30 ([13 §2](../docs/13-mods.md#2-the-owners-answers-2026-10-30)):
  - r2modman codes are used both ways.
  - Mods never update automatically.
  - Every mod of a code goes to the server.
  - Configs are viewable first and editable later.

## Decision

1. **Set folders outside the game install.**
   - Each mod set is built once into `instances\<id>\mods\sets\<set id>\`, together with its loader (BepInEx), and never changes after that.
   - The game folder gets only the loader's entry files: Doorstop's `winhttp.dll`, and a `doorstop_config.ini` that is off.
   - The loader is code in Core. When the instance's *Mods on* flag is set, the loader adds the launch arguments that point Doorstop at the active set. When the flag is off, it adds the arguments that turn Doorstop off.
   - Spike S-M1 must confirm this for `valheim_server.exe` before this ADR is accepted. If it fails, BepInEx goes into the game folder and a set is copied there on activation.
2. **Pinned sets that Core owns.**
   - SQLite holds:
     - each set, with exact versions, an enabled flag, and whether a mod was chosen or pulled in as a dependency;
     - the instance's active set, previous set and running set;
     - one trusted SHA-256 per package version. The first download records it, and every later download must match.
   - `mods.json` is only a copy.
3. **Changes make a new set; activation is a pointer.**
   - The `mods.sync` job resolves, downloads, verifies and builds the new set, then makes it active.
   - The running server is not stopped. The new set takes effect at the next start, the way settings do.
   - Going back one step swaps the active and previous pointers.
   - Nothing updates by itself.
   - Configs carry over when a set first starts.
4. **r2modman codes are the exchange format with players.** A code is imported to set a server's mods, and a code is made from the server's active set for friends.
5. **Sources and loaders are separate.** A loader (BepInEx first) is code in Core. A source (Thunderstore first) supplies packages. Palworld will add a source and a loader without changing the sets, the job or the panel.

## Consequences

- SteamCMD updates and file checks leave the mods alone. *Mods on / off* and going back one step are cheap and atomic.
- A mods change never stops a server and never holds the instance lock. So it avoids the start-after-job behaviour that [ADR-0018](0018-backups-leave-the-mvp.md) notes.
- A mod cannot rewrite the list that Core installs from.
- Disk use grows to at most three sets per instance (active, previous, running), plus their cached zips.
- Core depends on two Thunderstore endpoints that are not documented as stable: the package API and `legacyprofile`. If they change, importing and sharing codes stops working, but installed servers keep running.
- Trust on first use: a package that was already bad when it was first downloaded is not caught. Only people with `instance.mods.manage` change mods.
- Phase 1 of [13 §6](../docs/13-mods.md#6-plan) changes these docs and regenerates the OpenAPI client:
  - [03](../docs/03-domain-model.md) (`mods.sync`, the tables)
  - [04 §3 and §7](../docs/04-adapter-spec.md#7-mods)
  - [05](../docs/05-api.md)
  - [06 §5](../docs/06-security-and-access.md#5-authorization)
  - [07](../docs/07-ux-and-user-flows.md)
  - [08 §1](../docs/08-operations.md#1-filesystem-layout) (`mods\` layout)
  - [10 §8](../docs/10-build-test-release.md#8-manual-tests-on-the-vm) (VM checklist)

## Alternatives considered

- **Install BepInEx into the game folder**, as the pack's README says.
  - It is simpler.
  - But it mixes mods into SteamCMD's files, makes *off* mean deleting files, and makes going back a copy job while the server is stopped.
  - It stays the fallback if S-M1 fails.
- **One profile folder, renamed into place on each change.**
  - Two renames are not atomic. A crash or a locked file between them leaves no profile, and the server has to stop for the swap.
- **`mods.json` as the source of truth** (04 §7 as sketched).
  - It is a file the game's user can write, so a mod could change what Core installs next.
- **Always use the latest version.**
  - It breaks the exact version match that the group's mods need, and updates mods behind the Owner's back.
- **Only Thunderstore search, no codes.**
  - The friends would rebuild the list by hand in r2modman.
