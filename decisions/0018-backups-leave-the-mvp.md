# ADR-0018: Backups, schedules and safe updates leave the MVP; the Owner backs up by hand for now

- **Status:** Accepted (the Owner decided it on 2026-10-30). Amends the consequence "MVP milestones are M0–M4" of [ADR-0011](0011-mvp-scope-and-direction.md).
- **Date:** 2026-10-30

## Context

- [ADR-0011](0011-mvp-scope-and-direction.md) made M0–M4 the MVP. M3 holds backups, restore, schedules, the update flow as one job, and archive / purge; its exit is success criterion 4 ([01 §8](../docs/01-vision-and-scope.md#8-success-criteria)): restoring yesterday's backup works end to end.
- A readiness review of M3 on 2026-10-29 found that restore, as [08 §4](../docs/08-operations.md#4-backups) specifies it, cannot work under [ADR-0009](0009-data-root-acl-and-secrets.md). `gamewarden-run` has only Read on `instances\<id>\` itself (ADR-0009 §1, `DataRootAcl.PrepareInstance`), but ADR-0009 §3 has restores write the files as that user. So it can neither create the temporary folder next to `data\` nor swap it in. M3 would first need a new permission design, and a way to recover when Core stops in the middle of a swap.
- The Owner runs a few game servers. The games keep their own rolling backups next to their saves (Valheim: `-backups`, `-backupshort`, `-backuplong`, [04 §9](../docs/04-adapter-spec.md#9-example-valheim-l2)), and the Owner can copy a save folder by hand.
- On 2026-10-30 the Owner judged that the permission work is not worth it yet, and will take backups up again when they are needed.

## Decision

1. **M3 moves to the backlog** of the [ROADMAP](../ROADMAP.md) as "Backups, schedules, safe updates (was M3)". Milestone numbers stay as they are; the MVP is M0, M1, M2 and M4.
2. **Success criterion 4 leaves the MVP** with it, and becomes the exit of that work when it comes back.
3. **Until then the Owner backs up by hand:** stop the server from the panel, copy `instances\<id>\data\`, start it again. The games' own backups stay on.
4. **The design stays the target:** [08 §4](../docs/08-operations.md#4-backups), ADR-0011 decision 3 (one backup folder) and the backup endpoints in [05](../docs/05-api.md) are kept as they are, and not built.

## Consequences

- Restores are by hand. A game update (`update-game`) runs without a backup before it, and deleting a server is a purge only (no archive).
- Core's own state is not backed up either (OQ-27): copy `gamewarden.db` and `gamewarden.json` by hand when needed. The self-update still keeps the database snapshot it takes before an update ([10 §6](../docs/10-build-test-release.md#6-self-update-from-github-releases)).
- Nothing runs on a schedule, so the job-end problem below cannot happen by itself.
- When the work comes back, it starts from what the review found on 2026-10-29:
  - **Restore permissions:** who creates the temporary folder, grants `gamewarden-run` access to it and swaps it in (most likely SYSTEM, with a grant step, in a new ADR), and how a swap is finished or rolled back after a Core restart. The instance's active job is held only in memory (`InstanceActor`), so nothing records a half-done swap today.
  - **Faulted instances:** `InstanceActor.EndJobAsync` starts an instance whose desired state is *running* even when it is *Faulted*. A scheduled job would then start a Faulted server, against OQ-16.
  - **Busy instances:** [03 §5](../docs/03-domain-model.md#5-jobs) says jobs queue, the code answers 409 (`instance_busy`). What a scheduled job does on a busy instance is not specified.
  - **What a restore brings back:** settings live in SQLite (OQ-17), not in `data\`.
  - **Archive and purge:** archive is not specified; purge runs inside the request, so a backup before it makes it a job (202 instead of 200 for the CLI and the panel).
  - **Retention and low disk:** manual backups have no retention rule, and pruning on low disk can remove the last good backup.
  - **OQ-37:** look at the Valheim 1.0 world layout in the VM's `data\` before building backups.
  - **Order:** design (the ADR above, fixes to 03, 05 and 08) → jobs → backup → restore → retention and low disk → scheduler → update, archive and purge as jobs → panel, CLI and a release candidate.

## Alternatives considered

- **Build M3 now, with a new ADR for restore permissions** — the largest milestone so far, for what the games' own backups and a manual copy already cover for one Owner.
- **Backup without restore** — a zip that still has to be restored by hand, which is what the Owner does today anyway.
