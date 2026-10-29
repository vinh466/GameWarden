# ADR-0016: Windows as an integration layer: platform ports around a portable core

- **Status:** Accepted (the Owner approved it on 2026-10-29)
- **Date:** 2026-10-29

## Context

- On 2026-10-29 the Owner moved development from the Windows 11 PC to an Ubuntu 26.04 dev box shared with other projects ([ADR-0017](0017-develop-on-linux-test-windows-on-ci-and-the-vm.md)). GameWarden itself still runs only on Windows: the template VM is where it is deployed and tested by hand.
- Measured on the dev box on 2026-10-29 at `v0.2.0-rc.2` (`main` 6e751d7), with every project on `net10.0-windows`:
  - every project compiles, but the build stops at Core's build-time OpenAPI document, because Core's startup calls `WindowsIdentity` (`IsolationMode.From`);
  - `dotnet test` passes 111 of 171 tests. All 20 `Api` tests and the Supervisor and Job tests fail at Core's start (`WindowsIdentity`, ACL'd token files, DPAPI, kernel32 metrics). The Runner tests fail on kernel32. Four unit tests assume Windows paths or `cmd.exe`;
  - the panel and docs checks all pass.
- Some Windows interop already sits behind interfaces that the isolation mode selects: `IProcessHost`, `IInstanceFiles`, `IDataRootAcl`, `IFirewall` and `ISecretStore`.
- Other Windows interop is static and sits on ordinary paths:
  - CPU / RAM metrics and the resource check before a start (kernel32);
  - prerequisite checks (registry);
  - token files (ACLs) and the Data Protection keys (DPAPI);
  - the Runner's console trick, Job Object and pipe ACL;
  - Core's check of the Runner pipe's server PID.
- No code checks the OS. Because every project targets `net10.0-windows`, the platform analyzer (CA1416) never warns.
- The intent was already there. [02 §11](../docs/02-architecture.md#11-development-environment) planned fakes behind `IRunnerClient` and `IPrerequisiteChecker`, and [ADR-0012](0012-ci-and-tests-on-github-hosted-runners.md) decision 5 keeps Windows interop "thin and behind interfaces".
- Analyzer measurement on 2026-10-29: with `net10.0` instead of `net10.0-windows`, CA1416 flags exactly the files that use annotated Windows APIs. In Core those are Access, TokenFile, Prerequisites, CoreStartup, DataRootAcl, InstanceFiles, RunAsAccount, Firewall and SecretStore; in the Runner, PipeServer; and Setup. P/Invoke (kernel32, advapi32, user32) is not annotated, so CA1416 does not see it.

## Decision

1. **Portable core, Windows integration layer.** Core, the Runner, Contracts, the CLI and FakeGame target `net10.0`. Windows-specific behaviour lives in *adapters* behind *ports* (interfaces). Setup stays `net10.0-windows`: it is an installer for Windows and nothing else. The test projects target `net10.0-windows`, because they exercise both adapter sets and Setup's planning logic. They still run on Linux.
2. **One place chooses.**
   - Core picks its adapters in one composition root (`PlatformServices`), from the OS and the isolation mode.
   - The Runner picks its platform once at startup.
   - Portable code never calls a Windows API or a P/Invoke directly.
3. **The compiler guards the boundary.**
   - Every Windows adapter is marked `[SupportedOSPlatform("windows")]`.
   - With `TreatWarningsAsErrors`, CA1416 fails the build when portable code reaches a Windows-only API without an `OperatingSystem.IsWindows()` guard.
   - P/Invoke is invisible to CA1416, so P/Invoke declarations live only inside annotated adapter classes. The Linux CI job ([ADR-0017](0017-develop-on-linux-test-windows-on-ci-and-the-vm.md)) catches what still slips through.
4. **A development platform for Linux.** A second adapter set lets Core and the Runner run on Linux for development and tests.
   - It is **not a deployment target**. It has no isolation, firewall, service host, self-update or SteamCMD; these features answer with a clear error.
   - Its secrets, token files and keys are protected by file permissions (owner only), not by encryption.
   - Other operating systems are not supported.

   | Port | Windows adapter (production) | Development platform (Linux) |
   |---|---|---|
   | `IProcessHost` | `RunAsUserProcessHost` (isolation on), `CurrentUserProcessHost` | `CurrentUserProcessHost` |
   | `IInstanceFiles`, `IDataRootAcl`, `IFirewall` | impersonation, ACLs, firewall rules (isolation on); plain / none (off) | plain / none |
   | `ISecretStore` | DPAPI, CurrentUser scope ([ADR-0009](0009-data-root-acl-and-secrets.md)) | files readable only by their owner (mode 0600) |
   | `IMachine` (new): RAM, disk, CPU times, a process tree | `GlobalMemoryStatusEx`, `GetSystemTimes`, Toolhelp snapshot | `/proc` |
   | `IPrerequisiteChecker` (new) | registry and System32 files | reports every Windows prerequisite as not checked |
   | Token files (CLI, setup) | ACL: SYSTEM, Administrators, Core's account | mode 0600 |
   | Data Protection keys | DPAPI | file permissions only |
   | Service host | `WindowsServiceLifetime` ([ADR-0014](0014-service-host-with-preshutdown.md)) | none; `--service` is refused |
   | Runner: CTRL+C | console rules from spike S1 (02 §4.3) | `SIGINT` to the game |
   | Runner: child processes | Job Object, kill on close | `Process.Kill(entireProcessTree)` |
   | Runner pipe | named pipe with an ACL; server PID from `GetNamedPipeServerProcessId` (OQ-26) | Unix domain socket for the current user only; peer PID from `SO_PEERCRED` |

5. **Test tiers.** Each test class carries a `Category` trait.

   | Tier | What it covers | Runs on |
   |---|---|---|
   | `Unit` | Pure logic, with fakes where it meets a port | any OS |
   | `Api` | An in-process Core over HTTP | any OS |
   | `Integration` | Core, the Runner and FakeGame as real processes | any OS: Linux uses the development platform, Windows the real adapters |
   | `Windows` | The Windows adapters themselves: DPAPI, ACLs, the console trick, the Job Object, the pipe ACL and PID check, registry probes | Windows only; these tests skip themselves on other operating systems (`[WindowsFact]`) |
   | VM checklist | Admin rights, the service, reboots, real games ([10 §8](../docs/10-build-test-release.md#8-manual-tests-on-the-vm)) | by hand, unchanged |

   Tests of the development platform's own adapters (`/proc` probes, file modes, `SIGINT`, `SO_PEERCRED`) are marked `[LinuxFact]` and skip themselves on Windows. Parsing and other logic around an adapter is kept in pure functions, so it is tested in `Unit` on any OS.

## Consequences

- Most Core and panel bugs reproduce on the dev box. Core runs there with FakeGame, the panel's dev server talks to it, and the OpenAPI document is generated there.
- What is Windows-specific is visible by attribute and chosen in one place. A new Windows call in portable code fails the build, or at the latest the Linux CI job.
- There are two adapter sets to maintain. The development platform can hide Windows-only bugs (CTRL+C semantics, Job Object, ACLs, impersonation), so the `Windows` tier on `windows-2025` and the VM checklist stay the gate for those layers.
- Output folders move from `bin/<config>/net10.0-windows/` to `bin/<config>/net10.0/` for every project except Setup and the tests. Scripts and docs follow.
- Linux is still not a supported host. Making it one would be a new decision, revisiting [ADR-0002](0002-core-as-csharp-windows-service.md) and [ADR-0011](0011-mvp-scope-and-direction.md).

## Alternatives considered

- **Keep `net10.0-windows` and rely on review**: no compile-time guard. Windows calls would keep creeping into portable paths, which is how the startup ended up Windows-only.
- **Multi-target `net10.0` and `net10.0-windows`**: doubles every build and spreads `#if` through the code. Annotations plus guards give the same safety with one build.
- **Fakes only, no development platform**: the tests would not depend on the environment, but no Core would run on the dev box for panel work and bug reproduction, and the Runner's process handling would be tested only on Windows.
- **A Windows VM on the dev box** (`dockur/windows` over KVM): it runs, but takes 64 GB of disk by default on a shared box with 75 GB free. It offers Windows 10 LTSC rather than the VM's IoT SKU, and needs its own licence. The `windows-2025` runner already gives a Windows test run in about two minutes.
