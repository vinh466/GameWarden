# ADR-0012: CI tests what fits GitHub-hosted runners; Windows-bound behaviour is tested by hand on the VM

- **Status:** Accepted
- **Date:** 2026-10-27

## Context

- Much of GameWarden is Windows-specific: services, Job Objects, consoles, named pipes, ACLs, local users, the firewall, pre-shutdown.
- The repository is public. GitHub-hosted runners are free and unlimited for public repositories (Windows x64: 4 vCPU, 16 GB RAM, 14 GB SSD). GitHub recommends self-hosted runners only for private repositories, because pull requests from forks can run code on them.
- Hosted runners run Windows Server, not the template VM's Windows 10 IoT Enterprise LTSC 2021, have the VC++ runtime preinstalled, and cannot reboot inside a job. Making admin-level and real-game tests reliable there is real effort for a one-Owner, one-VM project.
- The dev machine runs Windows 11 Home (no Windows Sandbox, no Hyper-V).
- The Owner prefers to test Windows-bound behaviour by installing or updating the build on the VM and checking it by hand (2026-10-27).

## Decision

1. CI runs only on GitHub-hosted runners (`windows-2025` for .NET, `ubuntu-24.04` for the panel and docs). No self-hosted runner, not even on the game VM.
2. CI runs what fits a hosted runner: `Unit`, `Api` and `Windows` tests (no admin rights, no machine changes, `FakeGame` instead of real games), the panel checks and the docs checks.
3. Whatever needs admin rights, changes the machine, needs a reboot, a real game or the VM's SKU is tested by hand on the template VM after installing or updating to the build under test ([10 §8](../docs/10-build-test-release.md#8-manual-tests-on-the-vm)). No effort goes into forcing these into CI; a CI test that is flaky for Windows-specific reasons moves to the checklist.
4. Getting a build onto the VM is one command: every push to `main` produces an unsigned dev-build artifact, release candidates are signed GitHub pre-releases ([10 §7](../docs/10-build-test-release.md#7-getting-a-build-onto-the-vm-for-manual-tests)).
5. Windows interop stays thin and behind interfaces, so the logic around it is unit-tested.

## Consequences

- CI is fast, free and stable; it guards the logic and the Runner/console mechanics.
- Bugs in the launch chain, ACLs, service host, Setup engine, firewall and real games are found on the VM, which depends on running the checklist for every release candidate and for changes in those areas.
- No `WindowsAdmin` test category and no scheduled real-game workflow.

## Alternatives considered

- **Admin-level tests on hosted runners** (users, rights, ACLs, service, firewall, install/update/rollback) — possible, but costly to keep stable and still not the VM's SKU.
- **Weekly real-game smoke test in CI** — large downloads, Steam CDN flakiness; the VM test covers it.
- **Self-hosted runner on the game VM** — tests the real SKU, but fork pull requests could run code on the machine that hosts the games.
