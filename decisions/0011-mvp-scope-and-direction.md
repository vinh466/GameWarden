# ADR-0011: The MVP is a single-Owner tool; the project is open source

- **Status:** Accepted (closes OQ-07, OQ-08, OQ-13, OQ-23 and OQ-09 except the license, which is OQ-24). The consequence "MVP milestones are M0–M4" is amended by [ADR-0018](0018-backups-leave-the-mvp.md) (2026-10-30): backups (M3) left the MVP, and by [ADR-0019](0019-discord-joins-the-backlog-mods-next.md) (2026-10-30): Discord (M4) left it too
- **Date:** 2026-10-27

## Context

- The spec designs a multi-user panel: roles, scopes, invites, audit ([ADR-0006](0006-rbac-with-separate-owner.md)). Building all of it first delays the point where GameWarden beats hosting by hand ([ADR-0001](0001-build-own-platform.md)).
- The first and, for now, only panel user is the Owner. Friends only join the game servers, mostly over Radmin VPN.
- Usually one game server runs at a time; the VM's CPU and RAM can be raised when more are needed.
- A Linux plan (Pelican, in `gamehost-infra`) exists in the same workspace.
- The Owner wants the project public, so others with the same need can use it and CI can run on free GitHub-hosted runners.

## Decision

1. **MVP = one Owner.** The MVP has exactly one account (the Owner) and no roles, scopes, invites or audit page. The permission model of ADR-0006 stays the target design: every endpoint still declares its permission and the Owner holds all of them, so roles can be added later without changing endpoints.
2. **Valheim first.** Several instances may run at once. Before each start Core compares the adapter's `resources.min` and `resources.recommended` with the machine: below the minimum the start is blocked (the Owner may force it), below the recommendation it warns ([04 §3](../docs/04-adapter-spec.md#3-adapterjson)).
3. **Backups are collected in one folder** (`backups\` under the data root by default, configurable as `backupRoot`). Copying backups off the machine is out of scope; the operator syncs that folder with whatever tool they like.
4. **GameWarden is the active plan.** The Pelican/Linux plan is a technical reference only (launch parameters, config formats, per-game quirks), like LinuxGSM and WindowsGSM.
5. **Open source on GitHub, public**, with CI on GitHub Actions. The README states the problem, the direction and the scope, so the project is not mistaken for a general hosting panel. The license is still to be chosen (OQ-24).

## Consequences

- MVP milestones are M0–M4; accounts and roles move after the MVP (ROADMAP M6). M2 has a login for the one Owner.
- The data model keeps `RoleBinding.scope` and the permission checks, so the multi-user work is additive.
- Everything committed is public: no secrets, host names or personal data; measured facts about the template VM are fine.
- Until a license file exists the default copyright applies: others may read the repository but not reuse it.

## Alternatives considered

- **Build roles and invites in v1** — more to build before the first useful release, for users who do not exist yet.
- **Private repository** — limited free CI minutes, and it does not help others with the same need.
- **Keep the Windows and the Linux plan both active** — two diverging designs for one maintainer.
