# ADR-0027: Development releases move forward

- **Status:** Accepted
- **Date:** 2026-11-03
- **Supersedes in part:** [ADR-0013](0013-own-setup-and-update-engine.md) (rollback, previous-version switch and the database snapshot)

## Context

GameWarden is a personal tool that is built while it is used. There is no fixed release date and no promise of a
stable upgrade path yet. The Owner needs to put a build on the VM quickly, test it directly, and keep building. The
M2 test line already produced 19 `rc` tags in six days; treating each one as a release candidate made the release
list, the update channel and the UI heavier than the development flow needs.

The code carried a rollback path, a database snapshot, `gamewarden version use --restore-db` and a *Switch back*
card on the System page. None of them is used in the current flow. They also create two versions to keep, a snapshot
to clean up, and a *previous* version that can point at the build that just failed.

## Decision

1. **One development line.** Every released build is a GitHub pre-release, tagged `vX.Y.Z-dev.N`. There is no
   stable/rc/alpha distinction in the application while the development line is the only line.
2. **Updates move forward only.** Core downloads the newest release, verifies the signed manifest, extracts it, and
   hands over to that version's Setup engine. Setup switches the service, starts it, and reports success or failure.
   It does not restore a database, switch back to an older version, or keep a previous version.
3. **A failed release stays current.** The next update replaces it. Manual recovery is a fresh `Setup.exe` or
   `gamewarden update --from <zip> --allow-unsigned` from an Administrator terminal. Data may be reset; that is
   acceptable for this line.
4. **Release notes are the data contract.** Before an update the panel shows the GitHub release body. Each release
   notes one of: keep data, back up and re-import, or start clean. `docs/releases/TEMPLATE.md` is the template; the
   release workflow uses `docs/releases/<version>.md` when it exists and falls back to a safe default that tells the
   Owner what to back up.
5. **Keep the useful safety net.** The manifest signature, hash check, canonical version validation, health check,
   `last-result.json`, setup log and the rule "games keep running" stay. They protect against a bad download, not
   against a bad feature.
6. **Stable/community lines are future work.** If a stable line appears, it gets its own channel and compatibility
   rules then. The development code should not grow a channel switch before that line exists.

## Consequences

- Less code and less UI: no `SwitchBack`, no `/system/versions`, no DB snapshot, no `version use --restore-db`, no
  channel toggle, one installed version plus any version a Runner still uses.
- A release that does not start leaves the service on the failed version. The Owner fixes it by installing the next
  build; the old folder is kept until the next successful update.
- Release notes become required reading. The panel shows them before the confirmation, and the default fallback is a
  backup warning rather than silence.
- The version rules must be one source of truth: the tag, the package and Core's `Canonical` must accept the same
  strings. Otherwise a release can be published that the VM refuses.
- Rollback can return later with a stable line, but it is not a development feature.
