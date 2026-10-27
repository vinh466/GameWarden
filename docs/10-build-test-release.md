# 10 · Build, test, release and update

**Status: Accepted (2026-10-27)** — [ADR-0012](../decisions/0012-ci-and-tests-on-github-hosted-runners.md), [ADR-0013](../decisions/0013-own-setup-and-update-engine.md), [ADR-0014](../decisions/0014-service-host-with-preshutdown.md). Diagram: [10-flow-release-and-update](diagrams/10-flow-release-and-update.excalidraw).

## 1. Goals

1. Every push and pull request is built and tested automatically, at no cost, for everything a hosted runner tests well.
2. A version tag produces a GitHub Release with a `GameWarden-Setup-<version>.exe` that the Owner downloads on the VM and runs.
3. The VM updates itself from GitHub Releases, from the panel or the CLI, with automatic rollback; game servers keep running during the update.
4. Whatever needs admin rights, a reboot, a real game or the VM itself is tested by hand on the VM, after installing or updating to the build under test. Effort goes into making that loop fast (§7), not into forcing such tests into CI.

Facts this plan relies on:

| Fact | Source |
|---|---|
| GitHub-hosted runners are free and unlimited for public repositories; Windows x64 (`windows-2025`, `windows-2022`) has 4 vCPU, 16 GB RAM, 14 GB SSD | [GitHub docs: runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners), read 2026-10-27 |
| GitHub recommends self-hosted runners only for private repositories: pull requests from forks can run code on them | [GitHub docs: self-hosted runners](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners) |
| Unauthenticated REST API calls are limited to 60 per hour per IP; `releases/latest` returns the newest non-prerelease, non-draft release | [GitHub docs: rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api); releases API |
| Inno Setup may be removed from future runner images | [inno-setup action note](https://gitea.am-wd.de/actions/inno-setup/commit/4cae520566994fb97cd216216a8acdd11ded5c3c?files=src) |
| The dev machine runs Windows 11 Home: no Windows Sandbox, no Hyper-V | Measured 2026-10-27 |

## 2. Test strategy

**Rule:** a test belongs in CI when it runs on a hosted runner without admin rights, without changing the machine and without a real game. Everything else is a step of the VM checklist (§8). A CI test that turns flaky for Windows-specific reasons moves to the checklist instead of being fought.

| Level | Category trait | What it covers | Where |
|---|---|---|---|
| Unit | `Unit` | State machine and per-instance queue, reconciler and restart policy with a fake clock (`TimeProvider` / `FakeTimeProvider`), template rendering and argument escaping, config renderers (`ini`, `json`, `keyvalue`), every built-in adapter against the JSON Schema, permission checks, backup retention and verification, disk guard, version comparison, update-manifest signature check, the ACL entries Core computes (not applies), the Setup engine's step plan, service-control code → host stop mapping | Dev machine + CI |
| API | `Api` | `WebApplicationFactory` with a temporary SQLite file and fakes: endpoints, login, CSRF, host filtering, the local-address filter, `Origin` check on the hub, ProblemDetails codes | Dev machine + CI |
| Windows, no admin | `Windows` | Runner + `FakeGame` as the current user: CTRL+C with the flag reset, stdin stop, kill, Job Object kill-on-close, output streaming, readiness, pipe reattach after the Core process is killed, protocol handshake and pipe PID check | Dev machine + CI (`windows-2025`) |
| Panel | – | ESLint, TypeScript, Vitest; generated API types match Core's OpenAPI document | Dev machine + CI (Ubuntu) |
| Panel E2E (from M2) | – | Playwright against Core in console mode with `FakeGame` — kept only while it stays cheap and stable | CI (`windows-2025`) |
| On the VM | – | §8 | By hand |

Tested only on the VM: the launch chain as `gamewarden-run` (`LogonUser`, `CreateProcessAsUser`, profile), the applied data-root ACL, the Windows service and pre-shutdown, firewall rules and Radmin, the Setup engine's install / update / rollback / uninstall, prerequisite installers, and real game servers. The code keeps this Windows interop thin and behind interfaces (`IProcessHost`, `IInstanceFiles`, `IFirewall`, ...) so that the logic around it stays unit-testable.

`FakeGame` (`tests/FakeGame`, grown from [spikes/s1-ctrlc](../spikes/s1-ctrlc/)): prints log lines and a readiness line, answers stdin (`save`, `exit`, `crash`, `hang`, `spam`), saves a "world" file on CTRL+C, and can hold a configurable amount of RAM.

## 3. CI workflows

| Workflow | Trigger | Jobs |
|---|---|---|
| `ci.yml` | push, pull request | **dotnet** (`windows-2025`): restore, build with warnings as errors, `Unit` + `Api` + `Windows` tests, upload `openapi.json` and test results; on pushes to `main` also package a **dev build** (zip + `Setup.exe`, unsigned) as a workflow artifact kept 14 days · **panel** (`ubuntu-24.04`): lint, typecheck, Vitest, build, API types vs `openapi.json` · **docs** (`ubuntu-24.04`): relative links, diagram files parse |
| `release.yml` | tag `v*` | the `ci.yml` jobs, then package, sign the manifest, attest, publish (§4) |
| CodeQL | default setup | C#, JavaScript/TypeScript |
| Dependabot | weekly | NuGet, npm, GitHub Actions |

Rules:

- GitHub-hosted runners only; never a self-hosted runner on this public repository.
- Pin runner images (`windows-2025`, `ubuntu-24.04`) and pin actions by commit SHA (Dependabot keeps them current).
- Default `permissions: contents: read`; only `release.yml` gets `contents: write`, `id-token: write`, `attestations: write`.
- The update-signing key lives only in the `release` environment, which only `v*` tags can use. Pull-request workflows never see secrets.
- Branch protection on `main`: pull requests with the `ci.yml` checks green; the Owner may bypass.

## 4. Versioning and release

- SemVer tags `vMAJOR.MINOR.PATCH`; release candidates `vX.Y.Z-rc.N` are published as GitHub pre-releases, which `releases/latest` ignores.
- The version comes from the tag (`-p:Version=`); untagged builds are `0.0.0-dev+<sha>`.

`release.yml` steps:

1. Run the `ci.yml` jobs.
2. `dotnet publish` Core, Runner, CLI and Setup self-contained for `win-x64` into one folder; the panel build goes into Core's `wwwroot`. Sharing one runtime copy between the four apps **[Unverified — check in M1; otherwise one runtime per app]**.
3. Zip it: `GameWarden-<v>-win-x64.zip`.
4. Build `GameWarden-Setup-<v>.exe`: the single-file Setup engine with the zip embedded (ADR-0013).
5. Write `SHA256SUMS` and sign it with the ECDSA P-256 update key → `SHA256SUMS.sig`.
6. Create build-provenance attestations for the zip and the setup exe (`actions/attest-build-provenance`).
7. `gh release create v<v> --generate-notes` with the four assets.

| Asset | Used by |
|---|---|
| `GameWarden-Setup-<v>.exe` | First install on the VM |
| `GameWarden-<v>-win-x64.zip` | Self-update |
| `SHA256SUMS` + `SHA256SUMS.sig` | Self-update verification; manual verification |

**Code signing.** At first, binaries are not Authenticode-signed: running the setup from a browser download shows SmartScreen's "Windows protected your PC" → *More info* → *Run anyway*. The checksums and attestations still let anyone verify a download (`gh attestation verify` on another machine). Later options:

- [SignPath Foundation](https://signpath.org/terms): free for OSI-licensed projects that are already released, built automatically from the repository, and publish a code signing policy; the certificate names SignPath Foundation as the publisher.
- [Azure Artifact Signing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options): about USD 9.99 per month; individuals only in the USA and Canada.

## 5. First install on the VM

1. On the VM, open the GitHub Releases page, download `GameWarden-Setup-<v>.exe`, run it (UAC prompt; SmartScreen while unsigned).
2. The Setup engine checks Windows version, admin rights, port 8765 and disk space, then:
   - copies the files to `C:\Program Files\GameWarden\versions\<v>\`;
   - creates `gamewarden-run` with its rights ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)) and the data root with its ACL ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md));
   - registers `GameWardenCore` (automatic delayed start, restart on failure, pre-shutdown timeout — [ADR-0014](../decisions/0014-service-host-with-preshutdown.md)), the panel firewall rule, a Start Menu shortcut and an *Apps & features* entry;
   - starts the service and opens `http://localhost:8765/setup?token=<one-time token>`.
3. `GameWarden-Setup-<v>.exe /quiet` does the same without questions.

```
C:\Program Files\GameWarden\
  versions\<version>\   core\  runner\  cli\  setup\  adapters-builtin\
  gamewarden.cmd        shim that runs the current version's CLI
```

## 6. Self-update from GitHub Releases

| Step | Rule |
|---|---|
| Check | Daily and on demand: `GET https://api.github.com/repos/vinh466/GameWarden/releases/latest` (unauthenticated; far below 60 per hour). A newer version raises `update.available` (panel badge, Discord). |
| Preconditions | Owner confirms; no job holds an instance lock or the SteamCMD lock; free disk ≥ 2 × package size. Otherwise 409 with the reason. |
| Download | Zip, `SHA256SUMS`, `SHA256SUMS.sig` into `C:\GameWarden\updates\<v>\` (SYSTEM only). |
| Verify | Signature of `SHA256SUMS` against the public keys compiled into Core (a list, so keys can rotate), then the zip hash. Any failure stops the update. |
| Prepare | Extract to `versions\<v>\`; snapshot the database (`VACUUM INTO updates\<v>\db-before.db`). |
| Apply | Core starts `versions\<v>\setup\gamewarden-setup.exe apply` detached. The Setup engine stops the service, switches its binary path to the new version, starts it and waits up to 120 s for `/healthz` to report the new version. |
| Roll back | On failure: stop, restore the DB snapshot, switch the binary path back, start the old version. |
| Games | Keep running: Runners are not in Core's job. Old Runner versions stay until their instance restarts; Core speaks the Runner pipe protocol of its own and the previous version. |
| Report | The Setup engine writes a result file; Core reads it on start → activity feed + Discord. |
| Clean up | Keep the last two versions plus any version a running Runner still uses. |
| CLI | `gamewarden update` (latest release) · `--prerelease` (newest release candidate) · `--version <v>` · `--from <zip> --allow-unsigned` (a dev build; local Administrator only) · `gamewarden version use <v>` (switch back by hand, with the DB snapshot of that update) |

Only the panel and API are down during an update, for as long as the service restart takes.

## 7. Getting a build onto the VM for manual tests

| Build | How it reaches the VM |
|---|---|
| Release candidate `vX.Y.Z-rc.N` (GitHub pre-release, signed) | On the VM: `gamewarden update --prerelease` |
| Any push to `main` (dev build, unsigned) | Download the `dev-build` artifact of that CI run, copy the zip to the VM, then `gamewarden update --from <zip> --allow-unsigned` |
| First install of a dev build | `Setup.exe` from the same artifact |

Back to the previous version at any time: `gamewarden version use <previous>`.

## 8. Manual tests on the VM

**When:** every release candidate, and after any change to the launch chain, ACLs, the service host, the Setup engine, firewall handling or an adapter. Run the steps the change touches; run all of them for a release candidate. Note the result in the pull request or the release notes.

Checklist (template VM, Windows 10 IoT Enterprise LTSC 2021):

1. **Install:** fresh `Setup.exe` (SmartScreen path), first-run wizard with the setup token, prerequisite installers (VC++, DirectX).
2. **Identity and files:** Valheim runs as `gamewarden-run` (Task Manager → *Details* → user name); `icacls C:\GameWarden` and one instance folder match ADR-0009; the game writes nothing outside `instances\<id>\` except the profile paths known from S3.
3. **Network:** a friend joins over Radmin VPN; the firewall rules allow only the VPN ranges; after a reboot the panel is reachable over the VPN even though the VPN adapter comes up late.
4. **Service:** restart `GameWardenCore` while Valheim runs → the game keeps running and Core reattaches.
5. **Shutdown:** reboot the VM with Valheim running → the world is saved (pre-shutdown) and the server is back up without login.
6. **Update:** update from the previous version through the panel; then the rollback drill (`gamewarden version use <previous>`).
7. **Backup:** restore yesterday's backup.

## 9. Special solutions and why they are acceptable

| Need | Solution | Why it is acceptable |
|---|---|---|
| Save worlds when Windows shuts down | Own service host that accepts `SERVICE_CONTROL_PRESHUTDOWN` ([ADR-0014](../decisions/0014-service-host-with-preshutdown.md)); .NET's `ServiceBase` / `WindowsServiceLifetime` only handle `SHUTDOWN` | Small, isolated P/Invoke code; the control-code mapping is unit-tested; checklist step 5 verifies it |
| Install, update, rollback, uninstall | Own Setup engine in C#, no Inno Setup / WiX ([ADR-0013](../decisions/0013-own-setup-and-update-engine.md)) | Install steps are custom anyway (user, rights, ACL, service, firewall); one code path; the step plan is unit-tested, checklist steps 1 and 6 verify the real thing |
| Trust the downloaded update | `SHA256SUMS` signed with our own ECDSA key; public keys compiled into Core | Free, no third party, verifiable with .NET's built-in `ECDsa`; Authenticode can be added later |
| Windows-bound behaviour (admin, service, reboot, real games, the VM's SKU) | Tested by hand on the VM (§8), not in CI | One Owner, one VM; putting any build on the VM is one command (§7); CI covers the logic around it |
| SmartScreen warning | Unsigned at first, checksums + attestations | Personal tool; signing via SignPath once releases exist |
