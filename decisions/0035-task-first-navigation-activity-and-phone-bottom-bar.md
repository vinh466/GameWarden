# ADR-0035: Activity at the top level, with a four-item phone bottom bar

- **Status:** Accepted — the Owner asked for this option on 2026-11-23, after the agent's research note on Batch 4
  of [22](../docs/22-panel-ux-review.md). The measurement and the B-lite choice are the agent's work.
- **Date:** 2026-11-23

## Context

The panel has three top-level destinations — Dashboard, Servers and System — and System owns Jobs, Activity,
Adapters, Agents, Operations, Logs and Notifications. That grouping is itself a decision: commit `8440282`
(2026-11-04) made System the operations hub. On phones the three top-level destinations sit behind the hamburger
drawer, and Activity is four actions away from a server page (drawer → System → Section → Activity).

Batch 1 of [22](../docs/22-panel-ux-review.md) replaced the scrolling tab strip with a Section picker, so the
original trigger for the navigation experiment ("only if A leaves the tab strip slow") no longer fires. The review's
own evidence and the follow-up research point elsewhere:

- Hidden navigation is used less and later; on phones a visible navigation with four or fewer destinations is
  better than a drawer ([NN/g, 2016](https://www.nngroup.com/articles/hamburger-menus/)).
- Material Design 3 recommends a bottom navigation bar for **three to five** persistent destinations of equal
  importance ([Material 3](https://m3.material.io/components/navigation-bar/guidelines)).
- Server panels with a small global shell plus a per-server workspace: Pelican uses an app panel for the server list
  and a separate server panel ([Pelican docs](https://pelican.dev/docs/panel/advanced/plugins)); Pterodactyl keeps
  the per-server navigation inside the server context.
- Grafana's megamenu covers levels 1–3 and leaves in-page tabs for level 4
  ([Grafana Saga](https://grafana.com/developers/saga/patterns/navigation)).

Measured on 2026-11-23 on the mock panel at 390 px, starting from `/servers/valheim-main` (median of three runs).
The number of actions is the stable metric; wall-clock was not compared because each route loads its data in the
mock:

| Destination | Before: actions | After: actions |
| --- | --- | --- |
| Dashboard | 2 | 1 |
| Servers | 2 | 1 |
| System | 2 | 1 |
| Activity | 4 | 1 |

Jobs is deliberately not in the table: the header already opens Jobs in one action, so promoting it would not
improve the reach of any destination and would add a fifth item without measured gain.

## Decision

1. **Activity becomes a top-level destination** at `/activity`. Its route keeps the server and type filters. The old
   `/system/activity` path redirects to `/activity` with the same search, so bookmarks keep working.
2. **Jobs stays a System tab**, with the existing one-action header indicator. System keeps Jobs, Logs,
   Notifications, Agents, Operations, Adapters and its Overview; the tab order is by use:
   **Overview · Jobs · Logs · Notifications · Agents · Operations · Adapters**.
3. **Phones get a four-item bottom bar** — Dashboard, Servers, Activity, System — with an icon and a label each, an
   active state, and a safe-area inset. The mobile hamburger and drawer go away; the server quick list stays on the
   Servers page. Desktop keeps the 248 px sidebar with the same four destinations and the server list.
4. **One navigation model per screen.** Activity is removed from the System tabs; its old route redirects. The
   command palette and every in-page link point at the new path.

## Consequences

- Activity is one action away from every page on a phone; the other three destinations are visible instead of
  hidden. The phone no longer has a drawer to learn.
- System stays the setup and diagnostics hub; Jobs keeps its header shortcut and its System tab.
- New route, redirect, bottom bar component, one shell change; `docs/07 §5` and `docs/11 §3` change with the code.
- The bottom bar costs 64 px of height on phones; the safe-area padding keeps the home indicator clear.
- Rollback: revert the ADR and its PR; `/system/activity` still exists in older builds, and the new `/activity` path
  is an additive route, so no data changes are involved.

## Alternatives considered

- **Full Option B — promote Activity and Jobs** — rejected: Jobs already opens in one action from the header; on
  phones the extra item would still be hidden unless the bottom bar grew to five, and the measurement shows no
  destination-reach gain.
- **A only — keep the three-item drawer** — rejected by the Owner: Activity would stay four actions away on a
  phone, the slowest destination in the measurement.
- **Two-pane command center (Option C)** — too large for one Owner who already knows the servers; it breaks the
  simple page model.
