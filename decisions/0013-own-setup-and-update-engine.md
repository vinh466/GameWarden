# ADR-0013: Own setup and update engine; side-by-side versions; signed update manifest

- **Status:** Proposed (would close OQ-06 and replace the upgrade rows of 08 §3)
- **Date:** 2026-10-27

## Context

- The Owner wants to download an `.exe` from GitHub Releases onto the VM and install with it, and later update from GitHub Releases.
- Installing GameWarden is mostly custom work: create `gamewarden-run` and grant it a user right, set the data-root ACL, register the service with a pre-shutdown timeout, add a firewall rule. An installer tool (Inno Setup, WiX) would only copy files and call our code for the rest. Inno Setup may also disappear from GitHub's runner images.
- Updating is the same work minus the user and the data root, plus switching versions and rolling back. Core runs as SYSTEM, so the update path is the most privileged code path in the system: what it runs must be verified.
- Games must keep running while Core is replaced ([ADR-0003](0003-runner-process-per-instance.md)).

## Decision

1. **One Setup engine in C#** (`GameWarden.Setup`) implements install, update, rollback and uninstall. It ships twice: as `GameWarden-Setup-<v>.exe` (single file, the release zip embedded, needs admin) and inside each installed version (`versions\<v>\setup\`) for updates. No Inno Setup or WiX.
2. **Side-by-side versions.** Each version lives in `C:\Program Files\GameWarden\versions\<v>\`. Switching versions means changing the service's binary path, so rollback is the same operation in reverse. Keep the last two versions plus any version a running Runner still uses.
3. **Signed update manifest.** Each release has `SHA256SUMS`, signed with an ECDSA P-256 key that exists only as a secret of the `release` environment in GitHub Actions. Core has the public keys compiled in (a list, for rotation) and refuses any update whose signature or hash does not match.
4. **Update flow** ([10 §6](../docs/10-build-test-release.md#6-self-update-from-github-releases)): check `releases/latest` daily → Owner confirms → wait until no job holds a lock → download → verify → extract → DB snapshot → Setup engine stops the service, switches the binary path, starts it and waits for `/healthz` → on failure restore the snapshot and switch back.
5. Authenticode signing is not required; it can be added later (SignPath Foundation) without changing the flow.

## Consequences

- Install, update and rollback are one tested code path; the `WindowsAdmin` CI tests install the previous version, update, force a failed health check and roll back.
- During an update only the panel and API are down; games keep running on their current Runner version. Core must speak the pipe protocol of the previous Runner version (OQ-26).
- We own an installer: *Apps & features* entry, shortcuts and uninstall are our code.
- Losing the signing key means no updates until a new release adds a new key, installed once by hand with `Setup.exe`. Leaking it lets an attacker who can also publish a release push code to the VM; keep the key only in the protected environment.
- Without Authenticode, SmartScreen warns on first install.

## Alternatives considered

- **Inno Setup wrapper calling the CLI** — familiar wizard UI; adds a second toolchain and Pascal script, and does not help with updates.
- **WiX / MSI** — MSI upgrade semantics, but heavy for one service and hard to combine with "keep games running".
- **Overwrite files in place** — no clean rollback, and files in use by Runners block the copy.
- **Trust HTTPS and the release's own checksum** — anyone able to replace a release asset could also replace the checksum.
