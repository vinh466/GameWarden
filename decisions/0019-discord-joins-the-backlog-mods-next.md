# ADR-0019: Discord joins the backlog; the MVP is M0–M2; Valheim mods come next, then Palworld

- **Status:** Accepted (the Owner decided it on 2026-10-30). Amends [ADR-0018](0018-backups-leave-the-mvp.md) decision 1 ("the MVP is M0, M1, M2 and M4").
- **Date:** 2026-10-30

## Context

- After [ADR-0018](0018-backups-leave-the-mvp.md) the MVP was M0, M1, M2 and M4. M4 holds Discord webhook events with a live status message, `/healthz` and Windows Event Log entries, and player probes.
- On 2026-10-30 the Owner chose to make GameWarden's own features do what the group needs first, and to add Discord afterwards in one pass, which they expect to be cleaner and easier than adding it feature by feature.
- The Owner and their friends play Valheim with mods, often. GameWarden starts a vanilla server, so a modded server is still set up by hand.
- After mods, the Owner wants a second game, Palworld, to test the shared adapter model on a game that is configured through a file rather than launch arguments, so that more games can be added later on the same model.

## Decision

1. **M4 moves to the backlog** of the [ROADMAP](../ROADMAP.md) as "Discord notifications and monitoring (was M4)". Milestone numbers stay as they are.
2. **The MVP is M0, M1 and M2.** Success criteria ([01 §8](../docs/01-vision-and-scope.md#8-success-criteria)) 1 and 2 passed on the VM (M2 and M1 exits); criterion 3 passed for the automatic restart, and its Discord message waits with M4; criterion 5 was built in M1 and is part of the M1 walk-through ([10 §8.1](../docs/10-build-test-release.md#81-walk-through-for-an-m1-release-candidate-cli-only)). The M0 items still open stay where they are.
3. **Order of the next work:**
   1. **Mods for Valheim** (M7's Thunderstore provider, with BepInEx), designed first in a design document the Owner approves.
   2. **Palworld** (M5's `palworld` adapter with the renderers it needs), to harden the adapter model for further games.
   The rest of M5, M6, M7's Steam Workshop provider and M8 follow later, in an order the Owner sets then.

## Consequences

- No Discord messages: the Owner sees crashes, restarts and updates in the panel's activity feed and notifications only.
- No player counts yet (the panel shows `—`), and no `/healthz` for an outside checker. Player probes or `/healthz` can be pulled out of the backlog on their own if mods or Palworld need them.
- Mods come before the renderers and settings work of M5, so the mods design must not assume `ini` or `hook` renderers exist; it may add what it needs to the adapter spec ([04](../docs/04-adapter-spec.md)).

## Alternatives considered

- **Build M4 now, as planned** — the Owner prefers to integrate Discord once the features it reports on have settled.
- **Palworld before mods** — the group uses mods today, which makes mods the more useful next step.
