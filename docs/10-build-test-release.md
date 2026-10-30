# 10 · Build, test, release and update

**Status: Accepted (2026-10-27); tiers, CI jobs and VM builds proposed 2026-10-29** — [ADR-0017](../decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md) (superseding [ADR-0012](../decisions/0012-ci-and-tests-on-github-hosted-runners.md)), [ADR-0016](../decisions/0016-windows-as-an-integration-layer.md), [ADR-0013](../decisions/0013-own-setup-and-update-engine.md), [ADR-0014](../decisions/0014-service-host-with-preshutdown.md). Diagram: [10-flow-release-and-update](diagrams/10-flow-release-and-update.excalidraw).

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
| The dev machine is an Ubuntu 26.04 box shared with other projects (24 vCPU, 30 GiB RAM, 75 GB free disk). Until 2026-10-28 it was a Windows 11 Home PC (no Windows Sandbox, no Hyper-V) | Measured 2026-10-29 ([ADR-0017](../decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md)) |
| Docker on a Linux kernel cannot run Windows containers | [ADR-0016](../decisions/0016-windows-as-an-integration-layer.md), alternatives |

## 2. Test strategy

**Rule:** a test belongs in CI when it runs on a hosted runner without admin rights, without changing the machine and without a real game. Everything else is a step of the VM checklist (§8). A CI test that turns flaky for Windows-specific reasons moves to the checklist instead of being fought.

Test tiers follow the platform layer ([ADR-0016](../decisions/0016-windows-as-an-integration-layer.md)). `dotnet test --solution GameWarden.slnx` runs every tier on any OS: `Windows` tests are marked `[WindowsFact]` / `[WindowsTheory]` and skip themselves elsewhere, so the same command is green on the Linux dev box and complete on Windows.

| Level | Category trait | What it covers | Where |
|---|---|---|---|
| Unit | `Unit` | State machine and per-instance queue, reconciler and restart policy with a fake clock (`TimeProvider` / `FakeTimeProvider`), template rendering and argument escaping, config renderers (`ini`, `json`, `keyvalue`), every built-in adapter against the JSON Schema, permission checks, backup retention and verification, disk guard, version comparison, update-manifest signature check, the ACL entries Core computes (not applies), the Setup engine's step plan, service-control code → host stop mapping | Any OS: dev box + CI (Linux and Windows) |
| API | `Api` | `WebApplicationFactory` with a temporary SQLite file on the platform's adapters: endpoints, login, CSRF, host filtering, the local-address filter, `Origin` check on the hub, ProblemDetails codes | Any OS: dev box + CI (Linux and Windows) |
| Integration | `Integration` | Core, the Runner and `FakeGame` as real processes for the current user: start, stdin stop, CTRL+C / `SIGINT` stop, kill, output streaming, readiness, crash and restart policy, pipe reattach after the Core process is killed, protocol handshake and pipe PID check, SteamCMD jobs with `FakeGame` standing in for SteamCMD | Any OS: dev box + CI (Linux on the development platform, Windows on the real adapters) |
| Windows, no admin | `Windows` | The Windows adapters themselves: CTRL+C with the console flag reset, Job Object kill-on-close, the pipe ACL, DPAPI secrets, ACL'd token files, registry and kernel32 probes | Windows: CI (`windows-2025`), or a Windows dev machine |
| Panel | – | TypeScript, oxlint, oxfmt, stylelint, Vitest, build; generated API client matches Core's OpenAPI document | Dev box + CI (Ubuntu) |
| Panel E2E | – | `panel/e2e/smoke.mjs` (playwright-core with an installed browser: the runner's Edge by default, or `GW_BROWSER_CHANNEL` / `GW_BROWSER_PATH`, e.g. Chrome on the Linux dev box) against a Release Core in console mode with `FakeGame` and the built panel: first-run setup, create with the wizard, start, live console, stop, sign out. Kept only while it stays cheap and stable | CI (`windows-2025`, job `e2e`); `npm run e2e` against any test Core, on Windows or Linux |
| On the VM | – | §8 | By hand |

Tested only on the VM:

- the launch chain as `gamewarden-run` (`LogonUser`, `CreateProcessAsUser`, profile);
- the applied data-root ACL;
- the Windows service and pre-shutdown;
- firewall rules and Radmin;
- the Setup engine's install, update, rollback and uninstall;
- prerequisite installers;
- real game servers.

The code keeps this Windows interop in adapters behind ports (`IProcessHost`, `IInstanceFiles`, `IFirewall`, `IMachine`, ...), chosen in one composition root ([ADR-0016](../decisions/0016-windows-as-an-integration-layer.md)), so the logic around it is tested on any OS.

`FakeGame` (`tests/FakeGame`, grown from [spikes/s1-ctrlc](../spikes/s1-ctrlc/)):

- prints log lines and a readiness line;
- answers stdin: `save`, `exit`, `crash`, `hang`, `spam`;
- saves a "world" file on CTRL+C (`SIGINT` on Linux);
- can hold a configurable amount of RAM.

## 3. CI workflows

| Workflow | Trigger | Jobs |
|---|---|---|
| `ci.yml` | push, pull request | **dotnet-linux** (`ubuntu-24.04`): restore, build with warnings as errors, every test tier (`Windows` skips itself), the OpenAPI document matches the committed one · **dotnet** (`windows-2025`): restore, build with warnings as errors, the OpenAPI document matches the committed one, every test tier including `Windows`; on pushes to `main` also package a **dev build** (zip + `Setup.exe`, unsigned) as a workflow artifact kept 14 days · **e2e** (`windows-2025`) · **panel** (`ubuntu-24.04`): lint, typecheck, Vitest, build, API types vs `openapi.json` · **docs** (`ubuntu-24.04`): relative links, diagram files parse |
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
- Previews of a milestone still in progress are `vX.Y.Z-alpha.N`, also pre-releases, so they reach the VM through the signed update path (`gamewarden update --prerelease`) instead of an unsigned dev build. A release candidate means the milestone is complete. First one: `v0.2.0-alpha.1` (2026-10-28, M2 phases 1–2).
- From 2026-10-29 ([ADR-0017](../decisions/0017-develop-on-linux-test-windows-on-ci-and-the-vm.md) §4), tags move forward on one line. A small change or a test build gets the smallest bump (`v0.2.0-rc.2` → `v0.2.0-rc.3`). While the project is experimental, the VM may be reinstalled from scratch instead of keeping compatibility with an earlier test build.
- The version comes from the tag (`-p:Version=`); untagged builds are `0.0.0-dev+<sha>`.

`release.yml` steps:

1. Run the `ci.yml` jobs.
2. `dotnet publish` Core, Runner, CLI and Setup self-contained for `win-x64` into one folder; the panel build goes into Core's `wwwroot` (M2). The four apps share one runtime copy (checked 2026-10-28: the packaged Core, Runner and CLI ran the end-to-end smoke test from that folder). `build/package.ps1` does steps 2–5 except signing; `build/sign-manifest.ps1` signs.
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
   - creates the data root and `C:\ProgramData\GameWarden` with their ACL ([ADR-0009](../decisions/0009-data-root-acl-and-secrets.md));
   - registers `GameWardenCore` (automatic delayed start, restart on failure after 10/30/60 s, 120 s pre-shutdown timeout — [ADR-0014](../decisions/0014-service-host-with-preshutdown.md)), puts `gamewarden.cmd` on the machine PATH and adds an *Apps & features* entry;
   - starts the service and waits until Core reports the new version. On its first start Core creates `gamewarden-run` with its rights ([ADR-0008](../decisions/0008-run-as-user-and-launch-chain.md)).
   - From M2: the panel firewall rule, a Start Menu shortcut, and opening `http://localhost:8765/setup?token=<one-time token>`.
3. `GameWarden-Setup-<v>.exe install --quiet` does the same without questions; `--data-root <path>` picks another data root; `status` shows what is installed.

```
C:\Program Files\GameWarden\
  versions\<version>\   GameWarden.Core.exe  GameWarden.Runner.exe  gamewarden.exe  GameWarden.Setup.exe
                        adapters-builtin\  version.txt  (one shared .NET runtime)
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
| Apply | Core starts `versions\<v>\GameWarden.Setup.exe apply <v> --snapshot <db-before.db>` detached. The Setup engine stops the service, switches its binary path to the new version, starts it and waits up to 120 s for `/api/v1/system` to report the new version (`/healthz` deliberately says nothing about versions). |
| Roll back | On failure: stop, restore the DB snapshot, switch the binary path back, start the old version. |
| Games | Keep running: Runners are not in Core's job. Old Runner versions stay until their instance restarts; Core speaks the Runner pipe protocol of its own and the previous version. |
| Report | The Setup engine writes a result file; Core reads it on start → activity feed + Discord. |
| Clean up | Keep the last two versions plus any version a running Runner still uses. |
| CLI | `gamewarden update` (latest release) · `--check` · `--prerelease` (newest pre-release: release candidate or preview) · `--version <v>` · `--from <zip>` (verified when `SHA256SUMS` and `.sig` lie next to it) `--allow-unsigned` (a dev build; local Administrator only) · `gamewarden version` · `gamewarden version use <v> [--restore-db]` (switch back by hand; `--restore-db` also puts back the DB snapshot of that update) |

Only the panel and API are down during an update, for as long as the service restart takes.

## 7. Getting a build onto the VM for manual tests

| Build | How it reaches the VM |
|---|---|
| Any tag: release candidate `vX.Y.Z-rc.N` or preview `vX.Y.Z-alpha.N` (signed GitHub pre-release, §4) | On the VM: `gamewarden update --prerelease`, or a fresh install with that release's `Setup.exe` |
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

### 8.1 Walk-through for an M1 release candidate (CLI only)

M1 has no panel and no backups yet: step 1's wizard and prerequisite installers, the panel parts of steps 3 and 6, and step 7 wait for M2 and for backups (backlog, [ADR-0018](../decisions/0018-backups-leave-the-mvp.md)). Everything else goes through the CLI. Run the CLI from a terminal **opened as Administrator** (the CLI token is readable by Administrators only).

1. **Install.** Download `GameWarden-Setup-<v>.exe` from the release, run it, answer `Y`. Open a new Administrator terminal: `gamewarden system` shows the version, `isolation on (games run as gamewarden-run)`, the prerequisites, and free RAM and disk. A `MISSING vcredist-x64` line means the VC++ x64 runtime must be installed first (link in the line); `absent` lines are prerequisites no installed adapter needs. A `BLOCKED` host under *Network (DNS)* means the network's DNS hides it (seen with Vietnamese ISPs, [08 §2](08-operations.md#2-prerequisites)): set the VM's DNS to `1.1.1.1` before installing a game.
2. **Create and install Valheim.**
   ```powershell
   gamewarden create valheim-main --adapter valheim --set serverName=MyServer --set world=MyWorld
   gamewarden secret valheim-main password      # at least 5 characters, not part of the server name
   gamewarden install valheim-main --wait       # downloads SteamCMD, then the server (several minutes)
   gamewarden start valheim-main
   gamewarden status                            # starting -> running
   gamewarden logs valheim-main --tail 50
   ```
   `install` refuses below the adapter's free-disk need and `start` below its minimum CPU and RAM (`--force` overrides both); a missing setting or secret is refused with the command that sets it.
3. **Identity, files, firewall** (step 2 and 3 above): Task Manager → *Details*: `valheim_server.exe` and `GameWarden.Runner.exe` run as `gamewarden-run`; `icacls C:\GameWarden`, `icacls C:\GameWarden\instances\valheim-main` and `icacls C:\GameWarden\instances\valheim-main\data` match ADR-0009; *Windows Defender Firewall → Advanced → Inbound* has `GameWarden:valheim-main:2456/udp` and `:2457/udp` limited to the VPN ranges. A friend joins over Radmin VPN at `<radmin-ip>:2456` with the password.
4. **Crash:** end `valheim_server.exe` in Task Manager → `gamewarden status` shows `crashed`, then `running` again after the back-off (10 s).
5. **Service** (step 4): `Restart-Service GameWardenCore` → Valheim keeps running; `gamewarden status` shows it `running` with the same Runner PID.
6. **Shutdown and boot** (step 5): restart the VM with Valheim running → `C:\GameWarden\logs\core\` shows "Windows is shutting down: stopping 1 instance(s) gracefully", the world files under `instances\valheim-main\data\` have a fresh timestamp, and after boot (no login) `gamewarden status` shows `running` again.
7. **Game update:** `gamewarden update-game valheim-main --wait` → stop, SteamCMD, start again.
8. **Self-update and rollback** (step 6, from the second release candidate on): `gamewarden update --prerelease`, then `gamewarden version use <previous>` and back with `gamewarden update --prerelease`. `gamewarden version` and `C:\GameWarden\updates\last-result.json` show each outcome.
9. **Uninstall** (optional, last): *Apps & features → GameWarden → Uninstall* → the service, the firewall rules and the user `gamewarden-run` are gone; `C:\GameWarden` stays.

Logs for any problem: `C:\GameWarden\logs\core\`, `logs\jobs\`, `logs\setup\`, `instances\<id>\logs\` (game console) and `instances\<id>\logs\runner\runner.log`.

### 8.2 Results

| Date | Version | Steps | Result | Notes |
|---|---|---|---|---|
| 2026-10-28 | 0.1.0-rc.1 | 1–7 | Passed (Owner, template VM) | Install from the release `Setup.exe`; Valheim installed by SteamCMD and running as `gamewarden-run`; firewall rules in place and a friend joined over Radmin VPN; crash → automatic restart; `GameWardenCore` restart → game kept running; VM reboot → world saved and server back without login; `update-game` stopped, updated and restarted the server. The first install needed the VM's DNS set to `1.1.1.1` ([08 §2](08-operations.md#2-prerequisites)) and the VC++ x64 runtime. |
| 2026-10-28 | 0.1.0-rc.1 → 0.1.0-rc.2 | 8 | Passed (Owner, template VM) | `gamewarden update --prerelease` found, verified and applied rc.2; `gamewarden version use 0.1.0-rc.1` switched back and `update --prerelease` went forward again; Valheim kept running throughout. Step 9 (uninstall) not run yet. |
| 2026-10-28 | 0.1.0-rc.2 → 0.2.0-alpha.1 | 8.3 | Passed (Owner, template VM) | Updated with `gamewarden update --prerelease`; setup link, Owner account and the panel worked. |
| 2026-10-28 | 0.2.0-alpha.2 → alpha.3 | Live data | Passed (Owner, template VM) | Updated from the panel; live console, state changes made from the CLI, activity feed, CPU / RAM. |
| 2026-10-28 | 0.2.0-alpha.4 | Network settings, switch back | Passed (Owner, template VM) | Updated from the panel. |
| 2026-10-28 | 0.2.0-rc.1 | M2 release candidate | Passed (Owner, template VM) | Updated from the panel. Checked: the panel over Radmin from another PC and from a phone, toasts and tab status while starting and stopping, server actions from Ctrl+K, the notice after `GameWarden.Core` was killed and the service started it again. |
| 2026-10-29 | 0.2.0-rc.2 | 8.4 steps 1–7 | Passed (Owner, template VM) | "Works as I want". With *No map* on, the game had no map. What step 4 showed for OQ-35 and step 5 for OQ-36 was not recorded. |
| 2026-10-29 | 0.2.0-rc.4 | Help drawer ([12](12-explanations.md)) | Passed (Owner, template VM) | The Owner: very good. |
| 2026-10-29 | 0.2.0-rc.5 | 8.4 step 8 | Passed (Owner, template VM) | *Show in Valheim's server list* on works. The server was not in Valheim's community list: the VM has no port forwarding. Earlier hosting by hand: friends on the VPN could not join with `-public 0` (OQ-38). |

### 8.3 Walk-through for panel access (M2 phase 2)

Update the VM to the M2 preview: `gamewarden update --prerelease` installs `v0.2.0-alpha.1` or newer (signed, like a release candidate). A build of `main` between previews goes on with `gamewarden update --from <zip> --allow-unsigned` ([§7](#7-getting-a-build-onto-the-vm-for-manual-tests)).

1. **Setup:** in an Administrator terminal on the VM, `gamewarden setup-url`. First open that link from another PC over Radmin, with the VM's Radmin IP instead of `localhost` → refused (*create the Owner on the GameWarden machine itself*). Then open it in a browser on the VM and create the Owner (password of at least 10 characters) → the dashboard opens. `gamewarden setup-url` now answers that the Owner exists.
2. **Firewall:** *Inbound rules* has `GameWarden:panel:8765/tcp`, limited to the VPN ranges.
3. **Over the VPN:** from another PC or a phone on Radmin VPN, `http://<radmin-ip>:8765` → the sign-in page; sign in → the dashboard; start and stop a server.
4. **Not elsewhere:** from another PC on the LAN, `http://<lan-ip>:8765` gets no answer (the connection is closed); on the VM, `http://localhost:8765` works.
5. **Throttling:** five wrong passwords → *Too many failed sign-ins*; the right password works again after 15 minutes.
6. **Sessions:** sign in on two devices; change the password from the account menu on one → the other is signed out. `gamewarden owner reset-password` → both are signed out; the new password works.
7. **Reboot:** restart the VM → the panel answers over Radmin again without anyone logging in on the VM, although the VPN adapter starts after the service.

### 8.4 Walk-through for Valheim settings (L2)

On the VM with a build that has the Valheim adapter 0.3.0 (`gamewarden update --prerelease`, or `gamewarden update --from <zip> --allow-unsigned` for a build of the branch, [§7](#7-getting-a-build-onto-the-vm-for-manual-tests)). To see Valheim's command line: `Get-CimInstance Win32_Process -Filter "Name='valheim_server.exe'" | Select-Object -ExpandProperty CommandLine` in an Administrator terminal (it shows the password).

1. **Existing server:** after the update `valheim-main` runs as before. Its Settings tab has *Server*, *World*, *World modifiers* and a closed *Saving*, with the stored name and world and the password *Set*; *No unsaved changes*.
2. **Wizard:** *New server* → Valheim → name → *Server* (name in Valheim, password) → *World* (world name, preset *Hard*) → review → *Create & install*. Started, its command line has `-preset hard`, `-saveinterval 600` and no `-modifier`, `-setkey` or `-crossplay`.
3. **Modifiers:** *Raids* → *None* and *No map* on → *Save* → *Restart now*. The command line ends with `-modifier raids none -setkey nomap`; in the game there is no map.
4. **Back to normal (OQ-35):** *Raids* → *Normal*, *No map* off → save, restart. In the game: is the map back? Record the answer in OQ-35.
5. **Crossplay (OQ-36):** *Crossplay* on (the warning shows) → save, restart. The console shows a join code; join with it and the password from a PC that is **not** on the VPN. Record whether it works and whether anything listens on game port + 2 (`Get-NetUDPEndpoint -LocalPort <game+2>`). Turn crossplay off afterwards.
6. **Checks:** a 3-character password or a save interval of 10 shows the error under the field and nothing is saved.
7. **Manual:** compare `instances\<id>\server\Valheim Dedicated Server Manual.pdf` with the table in [04 §9](04-adapter-spec.md#9-example-valheim-l2) (preset names, `-instanceid`) and remove the **[Unverified]** marks it confirms.
8. **Server list (adapter 0.3.2, on by default since 0.3.4):** a server that never changed *Show in Valheim's server list* has `-public 1` once on its command line. Turn it off → save, restart: `-public 0` instead, and a friend on the VPN tries to join with *Join IP* (record the result in OQ-38). Turn it back on.

## 9. Special solutions and why they are acceptable

| Need | Solution | Why it is acceptable |
|---|---|---|
| Save worlds when Windows shuts down | Own service host that accepts `SERVICE_CONTROL_PRESHUTDOWN` ([ADR-0014](../decisions/0014-service-host-with-preshutdown.md)); .NET's `ServiceBase` / `WindowsServiceLifetime` only handle `SHUTDOWN` | Small, isolated P/Invoke code; the control-code mapping is unit-tested; checklist step 5 verifies it |
| Install, update, rollback, uninstall | Own Setup engine in C#, no Inno Setup / WiX ([ADR-0013](../decisions/0013-own-setup-and-update-engine.md)) | Install steps are custom anyway (user, rights, ACL, service, firewall); one code path; the step plan is unit-tested, checklist steps 1 and 6 verify the real thing |
| Trust the downloaded update | `SHA256SUMS` signed with our own ECDSA key; public keys compiled into Core | Free, no third party, verifiable with .NET's built-in `ECDsa`; Authenticode can be added later |
| Windows-bound behaviour (admin, service, reboot, real games, the VM's SKU) | Tested by hand on the VM (§8), not in CI | One Owner, one VM; putting any build on the VM is one command (§7); CI covers the logic around it |
| SmartScreen warning | Unsigned at first, checksums + attestations | Personal tool; signing via SignPath once releases exist |
