# ADR-0025: SteamCMD repairs a stuck manifest after `0x6` instead of retrying it

- **Status:** Accepted (the Owner decided it on 2026-11-03).
- **Date:** 2026-11-03

## Context

- On 2026-11-02 the `valheim-main` update jobs failed in about seven seconds with `Error! App '896660' state is 0x6 after update job.`; the Steam login before it answered `OK`, the VM had 23.7 GB free, and the running server was still `Valheim 1.0.20` after patch 1.0.21 shipped on 2026-10-28.
- `0x6` is `UpdateRequired` (2) + `FullyInstalled` (4): Steam knows the app is installed and a newer build exists, but the update job cannot start. A SteamCMD anonymous login only gets the manifest of the current build; the local `appmanifest_<app>.acf` names the older one, and Steam no longer serves it.
- This is the failure the Valheim server Docker project fixed in [PR #812](https://github.com/community-valheim-tools/valheim-server-docker/pull/812): move the manifest aside and run `app_update validate`. The installed files stay; Steam re-verifies them and downloads only what changed. Keeping the same `app_update` command, as GameWarden did for its three tries, cannot recover.
- GameWarden already keeps games in `server\`, worlds in `data\` and mods in `mods\sets\`, so the repair never touches a world or a mod set.

## Decision

1. **Repair once.** When an `app_update` fails and its output contains `state is 0x6`, Core moves `server\steamapps\appmanifest_<appid>.acf` to `appmanifest_<appid>.acf.bak-<UTC timestamp>` as `gamewarden-run`, then retries the same attempt with `validate` instead of the same command.
2. **Keep the old manifest.** The move is logged with both names; the backup stays in `steamapps\` for inspection. If the move fails or no manifest exists, the normal retry policy runs unchanged and the job reports the original error.
3. **Only `0x6` repairs.** `0x202` (disk or licence) stays permanent; a failed network run still retries the normal way.
4. **More detail on failure.** After the last failed attempt, the job log also carries the tail of SteamCMD's `logs\stderr.txt` and `logs\content_log.txt`, which hold the error detail stdout leaves out.
5. **The repair runs under the locks it already holds:** the instance job lock and the global SteamCMD lock. It never runs while the game is up: an update stops the game first, as before.

## Consequences

- A Valheim patch no longer needs the Owner to move a manifest by hand. The automatic path is the one the community fix uses.
- A repair may re-download files that `validate` finds different; usually only the changed packages. It is slower than a no-op update, not slower than a clean reinstall.
- The backup sits inside the game folder and is not part of the depot; SteamCMD ignores it. A later manual reinstall that deletes `server\` takes it with it.
- Old backups are not pruned in this change. They are a few KB; a release can prune `appmanifest_*.acf.bak-*` older than, say, 30 days if they accumulate.
- The job stays `failed` only if the repair itself fails; the Owner sees the original `0x6` message plus the repair lines in the log.

## Alternatives considered

- **Always pass `validate` on updates.** It makes every update read the whole install and does not by itself fix the stale manifest, so the `0x6` would remain.
- **Delete the manifest.** Same effect, but loses the evidence of which build the server had. Moving is as easy and keeps it.
- **Tell the Owner to repair it by hand.** The failure recurs on Valheim patches; a warden should heal a known, bounded state itself and say what it did.
- **Reinstall into a new folder on `0x6`.** The community fallback when the manifest move is not enough. It is heavier and belongs behind an explicit *Reinstall* action, not an automatic retry.
