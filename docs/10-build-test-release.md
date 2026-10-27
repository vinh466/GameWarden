# 10 · Build, test, release and update

**Status: Proposed (2026-10-27)** — [ADR-0012](../decisions/0012-ci-and-tests-on-github-hosted-runners.md), [ADR-0013](../decisions/0013-own-setup-and-update-engine.md), [ADR-0014](../decisions/0014-service-host-with-preshutdown.md). Diagram: [10-flow-release-and-update](diagrams/10-flow-release-and-update.excalidraw).

## 1. Goals

1. Every push and pull request is built and tested on Windows automatically, at no cost.
2. A version tag produces a GitHub Release with a `GameWarden-Setup-<version>.exe` that the Owner downloads on the VM and runs.
3. The VM updates itself from GitHub Releases, from the panel or the CLI, with automatic rollback; game servers keep running during the update.

Facts this plan relies on:

| Fact | Source |
|---|---|
| GitHub-hosted runners are free and unlimited for public repositories; Windows x64 (`windows-2025`, `windows-2022`) has 4 vCPU, 16 GB RAM, 14 GB SSD | [GitHub docs: runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners), read 2026-10-27 |
| GitHub recommends self-hosted runners only for private repositories: pull requests from forks can run code on them | [GitHub docs: self-hosted runners](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners) |
| Unauthenticated REST API calls are limited to 60 per hour per IP; `releases/latest` returns the newest non-prerelease, non-draft release | [GitHub docs: rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api); releases API |
| Hosted Windows runners run jobs as an administrator **[Unverified: confirm in the first CI run]** | GitHub docs |
| Inno Setup may be removed from future runner images | [inno-setup action note](https://gitea.am-wd.de/actions/inno-setup/commit/4cae520566994fb97cd216216a8acdd11ded5c3c?files=src) |
| The dev machine runs Windows 11 Home: no Windows Sandbox, no Hyper-V | Measured 2026-10-27 |

## 2. Test strategy

| Level | Category trait | What it covers | Where |
|---|---|---|---|
| Unit | `Unit` | State machine, reconciler and restart policy with a fake clock (`TimeProvider` / `FakeTimeProvider`), template rendering and argument escaping, config renderers (`ini`, `json`, `keyvalue`), every built-in adapter against the JSON Schema, permission checks, backup retention, version comparison, update-manifest signature check | Dev machine + CI |
| API | `Api` | `WebApplicationFactory` with a temporary SQLite file and fakes: endpoints, login, CSRF, host filtering, `Origin` check on the hub, ProblemDetails codes | Dev machine + CI |
| Windows | `Windows` | Runner + `FakeGame` as the current user: CTRL+C with the flag reset, stdin stop, kill, Job Object kill-on-close, output streaming, readiness, pipe reattach after the Core process is killed, protocol version handshake | Dev machine + CI |
| Windows admin | `WindowsAdmin` | Needs admin and changes the machine: temporary local user + `SeBatchLogonRight` → full ADR-0008 launch chain; data-root ACL applied and checked, including a planted-junction test; Windows service install → instance running → service stop (game survives) → start → reattach; firewall rules; setup engine: install previous version → update → forced health failure → rollback → uninstall | **CI only** (or a throwaway VM) |
| Panel | – | ESLint, TypeScript, Vitest; generated API types match Core's OpenAPI document | CI (Ubuntu) + dev machine |
| Panel E2E (from M2) | – | Playwright against Core in console mode with `FakeGame` | CI (Windows) |
| Real-game smoke | – | SteamCMD (anonymous) installs the Valheim dedicated server; start through the service and a Runner; wait for readiness; CTRL+C; the world file is saved; sample CPU/RAM for `resources.min` | CI, weekly + on demand, **does not block merges** |
| VM acceptance | – | Checklist on the template VM (§8) | Manual, per release candidate |

`FakeGame` (`tests/FakeGame`, grown from [spikes/s1-ctrlc](../spikes/s1-ctrlc/)): prints log lines and a readiness line, answers stdin (`save`, `exit`, `crash`, `hang`, `spam`), saves a "world" file on CTRL+C, and can hold a configurable amount of RAM.

On the dev machine: `dotnet test --filter "Category!=WindowsAdmin"`. Admin tests change users, rights, services and the firewall, and Windows 11 Home has no sandbox to contain them, so they run in CI.

## 3. CI workflows

| Workflow | Trigger | Jobs |
|---|---|---|
| `ci.yml` | push, pull request | **dotnet** (`windows-2025`): restore, build with warnings as errors, `Unit` + `Api` + `Windows` tests, upload `openapi.json` and test results · **admin-integration** (`windows-2025`): `WindowsAdmin` tests · **panel** (`ubuntu-24.04`): lint, typecheck, Vitest, build, API types vs `openapi.json` · **docs** (`ubuntu-24.04`): relative links, diagram files parse |
| `release.yml` | tag `v*` | everything in `ci.yml`, then package, sign the manifest, attest, publish (§4) |
| `smoke-real-games.yml` | weekly schedule, manual | real-game smoke (§2) |
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

1. Run all `ci.yml` jobs.
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
| Check | Daily and on demand: `GET https://api.github.com/repos/vinh466/GameWarden/releases/latest` (unauthenticated; far below 60 per hour). A pre-release channel can be switched on in System settings. A newer version raises `update.available` (panel badge, Discord). |
| Preconditions | Owner confirms; no job holds an instance lock or the SteamCMD lock; free disk ≥ 2 × package size. Otherwise 409 with the reason. |
| Download | Zip, `SHA256SUMS`, `SHA256SUMS.sig` into `C:\GameWarden\updates\<v>\` (SYSTEM only). |
| Verify | Signature of `SHA256SUMS` against the public keys compiled into Core (a list, so keys can rotate), then the zip hash. Any failure stops the update. |
| Prepare | Extract to `versions\<v>\`; snapshot the database (`VACUUM INTO updates\<v>\db-before.db`). |
| Apply | Core starts `versions\<v>\setup\gamewarden-setup.exe apply` detached. The Setup engine stops the service, switches its binary path to the new version, starts it and waits up to 120 s for `/healthz` to report the new version. |
| Roll back | On failure: stop, restore the DB snapshot, switch the binary path back, start the old version. |
| Games | Keep running: Runners are not in Core's job. Old Runner versions stay until their instance restarts; Core speaks the Runner pipe protocol of its own and the previous version (OQ-26). |
| Report | The Setup engine writes a result file; Core reads it on start → activity feed + Discord. |
| Clean up | Keep the last two versions plus any version a running Runner still uses. |
| Manual | `gamewarden update [--version <v>]`, `gamewarden version use <v>` (switch back by hand, with the DB snapshot of that update). |

Only the panel and API are down during an update, for as long as the service restart takes.

## 7. Repository hygiene (public)

- [LICENSE](../LICENSE) (MIT); `SECURITY.md` with GitHub private vulnerability reporting switched on — Core runs as SYSTEM and accepts network input.
- Secret scanning with push protection; CodeQL; Dependabot (§3).
- Nothing machine-specific in commits: no secrets, host names, VPN addresses or Discord webhook URLs.

## 8. What cannot be automated

Hosted runners run Windows Server 2025 / 2022, not the template VM's Windows 10 IoT Enterprise LTSC 2021, have the VC++ runtime preinstalled, and cannot reboot inside a job. A release candidate therefore goes through this checklist on the template VM before it is published as a full release:

1. Fresh install from `Setup.exe` (SmartScreen path), first-run wizard, prerequisite installers (VC++, DirectX).
2. Create `valheim-main`, a friend joins over Radmin VPN (firewall rule, network profile — spike S7).
3. Reboot the VM with the server running: world saved (pre-shutdown), server back up without login.
4. Update from the previous release through the panel; rollback drill (`gamewarden version use <previous>`).
5. Restore yesterday's backup.

## 9. Special solutions and why they are acceptable

| Need | Solution | Why it is acceptable |
|---|---|---|
| Save worlds when Windows shuts down | Own service host that accepts `SERVICE_CONTROL_PRESHUTDOWN` ([ADR-0014](../decisions/0014-service-host-with-preshutdown.md)); .NET's `ServiceBase` / `WindowsServiceLifetime` only handle `SHUTDOWN` | Small, isolated P/Invoke code with unit tests; verified on the VM by the reboot step of §8 |
| Install, update, rollback, uninstall | Own Setup engine in C#, no Inno Setup / WiX ([ADR-0013](../decisions/0013-own-setup-and-update-engine.md)) | Install steps are custom anyway (user, rights, ACL, service, firewall); one code path, tested end to end in CI |
| Trust the downloaded update | `SHA256SUMS` signed with our own ECDSA key; public keys compiled into Core | Free, no third party, verifiable with .NET's built-in `ECDsa`; Authenticode can be added later |
| Tests that change the machine | `WindowsAdmin` tests on throwaway hosted runners only | Runners are discarded after each job; the dev machine has no sandbox |
| Real games in CI | Weekly, non-blocking smoke job | Large downloads and the Steam CDN are slow and flaky; it must not block merges |
| The real VM SKU | Manual checklist per release candidate (§8) | No hosted runner uses this SKU; five steps, done once per release |
| SmartScreen warning | Unsigned at first, checksums + attestations | Personal tool; signing via SignPath once releases exist |
