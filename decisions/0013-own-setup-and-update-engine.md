# ADR-0013: Own setup and update engine; side-by-side versions; signed update manifest

- **Status:** Accepted (closes OQ-06; replaces the upgrade rows of 08 §3)
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
6. For manual tests on the VM the CLI also takes release candidates (`gamewarden update --prerelease`) and unsigned dev builds (`gamewarden update --from <zip> --allow-unsigned`, local Administrator only).

## Consequences

- Install, update and rollback are one code path: its step plan is unit-tested, and the real thing is exercised on the VM (checklist steps 1 and 6 in [10 §8](../docs/10-build-test-release.md#8-manual-tests-on-the-vm)).
- `--allow-unsigned` is a deliberate hole for the Owner's own dev builds; it needs a local Administrator on the VM, who could run any code anyway.
- During an update only the panel and API are down; games keep running on their current Runner version. Core must speak the pipe protocol of the previous Runner version (OQ-26).
- We own an installer: *Apps & features* entry, shortcuts and uninstall are our code.
- Losing the signing key means no updates until a new release adds a new key, installed once by hand with `Setup.exe`. Leaking it lets an attacker who can also publish a release push code to the VM; keep the key only in the protected environment.
- Without Authenticode, SmartScreen warns on first install.

## Alternatives considered

- **Inno Setup wrapper calling the CLI** — familiar wizard UI; adds a second toolchain and Pascal script, and does not help with updates.
- **WiX / MSI** — MSI upgrade semantics, but heavy for one service and hard to combine with "keep games running".
- **Overwrite files in place** — no clean rollback, and files in use by Runners block the copy.
- **Trust HTTPS and the release's own checksum** — anyone able to replace a release asset could also replace the checksum.

## Implementation notes (2026-10-28)

- `versions\<v>\` is one flat folder: Core, Runner, CLI and Setup are published self-contained into it and share one runtime copy (about 50 MB zipped). The Setup engine inside it is `GameWarden.Setup.exe`; `GameWarden-Setup-<v>.exe` is the same program published as a single file with the zip embedded (about 86 MB).
- Setup commands: `install` (default; an existing install is upgraded through `apply`), `apply <v> [--snapshot <db>] [--restore-db <db>]`, `uninstall [--remove-data]`, `status`. State lives in `HKLM\SOFTWARE\GameWarden`; each run logs to `<dataRoot>\logs\setup\`; the outcome goes to `<dataRoot>\updates\last-result.json`.
- The health check asks `/api/v1/system` for the version (with the CLI token), because `/healthz` says nothing about versions.
- `gamewarden-run` is created by Core on its first start as the service (and re-created if missing), not by Setup; Setup creates the data root with its ACL and the service.
- The first release key (ECDSA P-256) was created 2026-10-28; its public half is in `src/GameWarden.Core/Updates/UpdateKeys.cs`, its private half only in the `UPDATE_SIGNING_KEY` secret of the `release` environment, which accepts deployments from `v*` tags only. `build/sign-manifest.ps1` refuses to sign with a key whose public half is not compiled into Core.
