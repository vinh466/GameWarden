# Workflow

How much UX work a change needs depends on its size. Pick the tier first, then do only what the tier asks.

## Tier S: small change on an existing screen

Examples: add a field, rename a button, add a column, change a message.

1. Read the page for the component or pattern you touch (routing table in [SKILL.md](../SKILL.md)).
2. Match the screen's existing patterns: same components, spacing and wording as its neighbours.
3. Check the states your change touches: does the new field have an error state, does the new button have a pending state?
4. Run that page's checklist.
5. If the layout changed, do the [visual check](visual-check.md) for that screen.
6. Write the **UX notes** ([SKILL.md](../SKILL.md#report)). Keep them to two or three lines.

## Tier M: a new screen, dialog or form, or a large change to one

1. Read the product's UX rules (precedence in [SKILL.md](../SKILL.md#precedence)).
2. Write a **UX spec** with the [template](../templates/ux-spec.md), in the plan or the PR description. It takes
   10–20 lines: the user's task, the main action, the layout, the states, the components and the open questions.
3. Build it with the decision and pattern pages.
4. Do the [visual check](visual-check.md) with every state, at both widths and in both schemes.
5. Self-review with the [rubric](review-rubric.md) and fix every **blocker**.
6. Write the UX notes and any [UX proposals](ux-challenge.md).

## Tier L: a new flow, a feature area or a navigation change

1. Read the product's UX rules and the business requirements. List what is **fact**, what is **assumption** and
   what is **missing**.
2. [Research](research.md) how the problem is usually solved, then write a
   [research note](../templates/research-note.md) with 2–3 options:
   - **Option A (safe)**: built from the playbook's patterns and the product's existing ones.
   - **Option B / C**: bolder, if they serve the user better. Say what they cost.
3. Build **option A** unless the owner already chose. Put options B and C in the plan or PR as UX proposals.
4. Treat each screen as tier M: a UX spec, the visual check and the rubric.
5. In the UX notes, describe the flow end to end: entry points, the happy path, the failure paths and the exits.

## When to stop and ask

Ask before building only when:

- the spec would **lose data** or make an irreversible change without protection;
- the spec would **expose a secret** or personal data;
- the spec would **block** keyboard or screen-reader users from the main task;
- the spec **contradicts itself**, or a business rule is missing and guessing it would mean inventing it.

Otherwise, build the safe default and attach your proposal ([ux-challenge](ux-challenge.md)).

## Deliverables by tier

| | S | M | L |
| --- | --- | --- | --- |
| UX notes in the PR or report | ✓ | ✓ | ✓ |
| Checklist of the touched page | ✓ | ✓ | ✓ |
| UX spec |  | ✓ | ✓ for each screen |
| Visual check screenshots | if layout changed | ✓ | ✓ |
| Rubric self-review |  | ✓ | ✓ |
| Research note with options |  |  | ✓ |
| UX proposals | when you have one | when you have one | options B/C at least |
