# ADR-0012: CI and tests run on GitHub-hosted runners; admin tests only in CI

- **Status:** Proposed
- **Date:** 2026-10-27

## Context

- Most of GameWarden is Windows-specific: services, Job Objects, consoles, named pipes, ACLs, local users, the firewall.
- The repository is public. GitHub-hosted runners are free and unlimited for public repositories (Windows x64: 4 vCPU, 16 GB RAM, 14 GB SSD). GitHub recommends self-hosted runners only for private repositories, because pull requests from forks can run code on them.
- The dev machine runs Windows 11 Home, which has no Windows Sandbox and no Hyper-V to contain tests that create users, grant rights, install services or change the firewall.
- Hosted runners run Windows Server, not the template VM's Windows 10 IoT Enterprise LTSC 2021, and cannot reboot inside a job.

## Decision

1. All CI runs on GitHub-hosted runners (`windows-2025` for .NET, `ubuntu-24.04` for the panel and docs). No self-hosted runner, not even on the game VM.
2. Tests carry a category: `Unit`, `Api`, `Windows`, `WindowsAdmin`. `WindowsAdmin` tests (users, rights, ACLs, service, firewall, install/update/rollback) run only in CI. The others also run on the dev machine.
3. A weekly, non-blocking workflow installs and runs the real Valheim dedicated server through GameWarden.
4. Each release candidate passes a short manual checklist on the template VM before it becomes a full release ([10 §8](../docs/10-build-test-release.md#8-what-cannot-be-automated)).
5. `FakeGame` (from spike S1) is the test double for game servers.

Details: [10-build-test-release §2–3](../docs/10-build-test-release.md#2-test-strategy).

## Consequences

- Every change to the Windows-specific code is exercised on a real Windows machine with admin rights, for free.
- Admin-level bugs are found in CI, not on the dev machine; a failing `WindowsAdmin` test is debugged from logs (or a throwaway VM).
- SKU-specific problems (Windows 10 IoT LTSC, missing VC++/DirectX, Radmin) are caught only by the manual checklist.

## Alternatives considered

- **Self-hosted runner on the game VM** — tests on the real SKU, but fork pull requests could run code on the machine that hosts the games.
- **Run admin tests on the dev machine** — they would change its users, rights, services and firewall; Windows 11 Home has no sandbox.
- **Real games in every CI run** — large downloads, Steam CDN flakiness, slow feedback.
