# ADR-0021: Mod config files are edited in the panel, with revisions; they carry over when a set is built

- **Status:** Accepted (the Owner decided it on 2026-10-31). Amends [ADR-0020](0020-mods-in-set-folders-as-pinned-sets.md) decision 3 ("Configs carry over when a set first starts").
- **Date:** 2026-10-31

## Context

- 2026-10-31: the Owner ran the Valheim mods walk-through on the VM with `v0.2.0-rc.9`. The friends joined and the mods worked ([10 §8.2](../docs/10-build-test-release.md#82-results)).
- The Owner then asked to edit the mods' config files from the panel, with simple revisions. Until now they could only be viewed ([13 §4.7](../docs/13-mods.md#47-configs)).
- Today a set's configs are assembled at its **first start**: defaults, then the set that ran before, then the code's files. An edit made between a mods change and the next start would land in a set whose configs are about to be replaced. "The file I edit is the file the next start uses" would not hold.
- The Owner's answers (2026-10-31):
  - A text editor first; a form for BepInEx `.cfg` files can come later.
  - While the server runs, *Save* writes the file the game uses at once, as editing by hand does.
  - Revisions as proposed: each save is kept, the newest 50 per file, with history, a comparison with the current file, and restore.

## Decision

1. **The active set's `BepInEx/config` is the instance's config.** Configs carry over when a set is **built**, not at its first start. The new set's config folder is assembled in the build, in this order:
   1. the mods' default files;
   2. the current active set's config files;
   3. the code's files.

   Later files win. Activation then makes it the config the panel shows and edits.
2. **Editing.** In the active set's config folder, text files (`.cfg`, `.json`, `.yml`, `.yaml`, `.txt`, `.ini`) of up to 1 MB can be edited in the panel.
   - *Save* sends the new text and the SHA-256 of the text it started from. If the file on disk has changed since, the answer is 409 `config_changed` and nothing is written.
   - JSON is checked before it is written.
   - The file is written at once, as `gamewarden-run` (ADR-0009). When the active set is the one running, the game sees the change immediately. Mods that watch their file reload it; the others need a restart, and the panel says so.
3. **Revisions.** Core keeps revisions per instance and path in SQLite, out of `gamewarden-run`'s reach.
   - Each save stores the new text as an `edit` or a `restore`.
   - Before that, if the file on disk differs from the newest revision, Core stores what was on disk first. This kind is `server`: what the game, a mod update or a code had written.
   - The newest 50 revisions per file are kept.
   - The panel lists the revisions, compares one with the current file line by line, and restores one. A restore is a new revision.
   - Revisions belong to the path, so they survive set changes.

## Consequences

- The first-start step and `code-config/` go away: the build does that work, and `ModLaunch` only copies Doorstop's files and records the running set. The setting that tells a set's first start (`StartedAt`) is no longer needed for configs.
- What the game writes into the running set's configs after a newer set was activated is not carried over. The panel already shows *Restart to apply* in that state.
- The database grows by at most 50 × 1 MB per edited file. Real config files are a few KB.
- A structured editor for BepInEx `.cfg` files is not built yet.

## Alternatives considered

- **One config folder per instance, linked into every set by a directory junction.** No copying at all. But it needs a Windows junction port, Linux symlinks in development, and care with Core's defences against links planted in instance folders.
- **Copy configs in at start and out at stop.** A Core restart while the game runs would miss the copy back.
- **Apply edits only at the next start.** Safer, but never immediate. The Owner chose immediate.
