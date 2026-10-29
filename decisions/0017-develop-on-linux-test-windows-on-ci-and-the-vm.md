# ADR-0017: Develop on Linux; CI tests on Linux and Windows; Windows-bound behaviour by hand on the VM

- **Status:** Accepted (the Owner approved it on 2026-10-29). Supersedes [ADR-0012](0012-ci-and-tests-on-github-hosted-runners.md).
- **Date:** 2026-10-29

## Context

- Until `v0.2.0-rc.2` (2026-10-28) the Owner developed on a Windows 11 Home PC and tested on the template VM. From 2026-10-29 the Owner develops on an Ubuntu 26.04 dev box. That box is shared with other projects: it is also the homelab node that runs k3s, CI runners and other stacks. It has 24 vCPU, 30 GiB RAM and 75 GB of free disk (measured 2026-10-29).
- [ADR-0016](0016-windows-as-an-integration-layer.md) makes Core and the Runner portable and keeps Windows behind adapters. Every test tier except `Windows` runs on Linux.
- What [ADR-0012](0012-ci-and-tests-on-github-hosted-runners.md) relied on still holds:
  - GitHub-hosted runners are free and unlimited for public repositories.
  - GitHub recommends self-hosted runners only for private repositories, since pull requests from forks can run code on them.
  - Hosted runners are not the template VM's SKU and cannot reboot inside a job.
  - The Owner prefers to check Windows-bound behaviour by hand on the VM.
- Docker on a Linux kernel cannot run Windows containers. A Windows VM in a container (`dockur/windows`) needs KVM and 64 GB of disk by default ([ADR-0016](0016-windows-as-an-integration-layer.md), alternatives).
- Release candidates and milestone previews (`vX.Y.Z-alpha.N`) already reach the VM through the signed update path, `gamewarden update --prerelease` ([10 §4](../docs/10-build-test-release.md#4-versioning-and-release)). `release.yml` runs for any `v*` tag.

## Decision

1. **Dev machine.** Any OS with the .NET SDK pinned in `global.json` and Node.js LTS. The Owner's is the Ubuntu dev box. The game VM stays free of SDKs and dev tools. Nothing that changes a machine (Setup, services, users, firewall) runs on a dev machine.
2. **CI on GitHub-hosted runners only.** There is no self-hosted runner, not even on the game VM or the dev box.

   | Job | Runner | What it does |
   |---|---|---|
   | `dotnet-linux` | `ubuntu-24.04` | Builds; runs every test, where the `Windows` tier skips itself; checks that the OpenAPI document matches the committed one |
   | `dotnet` | `windows-2025` | Builds; runs every test, including the `Windows` tier |
   | `e2e` | `windows-2025` | Unchanged |
   | `panel`, `docs` | `ubuntu-24.04` | Unchanged |
   | `dev-build` | `windows-2025` | Unchanged |

3. **Unchanged from ADR-0012:** whatever needs admin rights, changes the machine, needs a reboot, a real game or the VM's SKU is tested by hand on the template VM ([10 §8](../docs/10-build-test-release.md#8-manual-tests-on-the-vm)). A CI test that is flaky for Windows-specific reasons moves to that checklist.
4. **Builds for the VM** (the Owner's rule, 2026-10-29).
   - Every build the VM tests is a signed pre-release from a tag. On the VM: `gamewarden update --prerelease`, or a fresh install from that release's `Setup.exe`. Nothing has to be copied to the VM.
   - Tags move forward on one line. A small change or a test build gets the smallest bump, e.g. `v0.2.0-rc.2` → `v0.2.0-rc.3`. There is no ceremony: the Owner is the only user.
   - The project is experimental, so the VM may be reinstalled from scratch. A clean change is preferred over keeping compatibility with an earlier test build.
   - Agents working for the Owner merge pull requests whose checks are green and push the next tag. Design documents that set direction still wait for the Owner.
   - The unsigned dev-build artifact of every push to `main` stays.
5. **A pull request names the layer it touches.**
   - Portable code only: CI is the check.
   - A Windows adapter, the Runner's Windows platform, the Setup engine or a game adapter's launch: the pull request lists the VM checklist steps to run, and their result is recorded as before.
6. **No Docker in the test loop.** The dev box's SDK runs the tests, and CI's Linux job is the clean environment. A container for a long-running development Core is an option for later, not part of this decision.

## Consequences

- Development happens where the Owner works. The inner loop (build, `Unit`, `Api`, `Integration`, panel, docs) runs on the dev box without Windows.
- Windows adapter changes get their first feedback from CI after a push, about two minutes for the `dotnet` job. Admin-level and real-game behaviour still needs the VM.
- Every VM test is a tagged pre-release. Pre-releases are never `latest` and are cheap to delete.
- The dev box is shared, so a development Core uses its own data root and ports (02 §11). Nothing in the test loop installs a service or touches other stacks.

## Alternatives considered

- **Keep developing on the Windows PC**: works, but it is not where the Owner wants to work, and it leaves the Windows-only startup in place.
- **Local Windows VM on the dev box** for Windows tests (`dockur/windows`): see [ADR-0016](0016-windows-as-an-integration-layer.md). Revisit if waiting for CI's Windows job becomes a bottleneck.
- **Self-hosted Windows runner**: rejected for a public repository, as in ADR-0012.
- **Copy unsigned branch builds to the VM**: needs a GitHub sign-in on the VM to download the artifact, plus a manual copy. A tag is one command on the VM.
