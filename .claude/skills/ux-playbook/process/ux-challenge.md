# UX proposals: how to challenge a spec, a flow or a component

You are expected to say when something could serve the user better: a confusing flow, a missing state, a component
the design system lacks, a pattern repeated so often it should become a shared component. This page says how to do
it without blocking the work and without changing business rules on your own.

## The rule: build the safe default, attach the proposal

1. **Build what was asked**, the safe way, with this playbook's patterns.
2. **Write the proposal** in the format below, in the plan, the PR description or the final report.
3. **Do not build the proposal** unless the owner accepts it. A proposal in code is not a proposal.
4. **Stop and ask first** only in the cases listed in [workflow](workflow.md#when-to-stop-and-ask): data loss, an
   exposed secret, an accessibility blocker, or a contradictory or missing business rule.

## When to raise one

- The flow takes more steps than the task needs, or makes the user remember something between screens.
- The spec leaves out a state: empty, error, loading, permission, offline.
- The spec asks for a pattern the [principles](../principles.md) argue against, such as a toast as the only error
  or hiding an unavailable action.
- A business rule makes the UI confusing, and a different presentation of the **same** rule would be clearer.
- Mantine and the product have no fitting component, and the same composition appears a second time.
- An existing shared component is wrong for this case, and changing it would help other screens.
- Research found a clearly better-known pattern for this problem.

Do not raise taste-only remarks ("I prefer blue"). Every proposal names a user problem.

## Format

```markdown
### UX proposal: <short title>

- **Problem**: <what the user struggles with, in one or two lines>
- **Evidence**: <a screenshot, a step count, a principle or pattern page, a source link, a measured fact>
- **Built now**: <what the safe default does>
- **Proposal**: <the change; for a component, its name, props and where it would live>
- **Options**: <A = built now, B = proposal, C = optional alternative; one line each with the trade-off>
- **Business impact**: <none, or exactly which rule it touches and how; never "probably fine">
- **Cost**: <S/M/L and what changes: files, a shared component, an API>
- **Decision needed**: <the yes/no or A/B/C question for the owner>
```

## Component requests

When you propose a new shared component:

- Name it for what it means (`EmptyState`, `StatusBadge`), not for how it looks (`GreyBox`).
- Put it where the product keeps shared components (its `components/` folder or UI package), never inline in a page.
- Build it on Mantine components and theme tokens. List the props, the states and the screens that would use it.
- Until the owner accepts it, a repeated composition may stay local. Mark it with a comment that points to the
  proposal.

## Facts, assumptions, proposals

Keep them apart in everything you write:

| Kind | Where it comes from | How to mark it |
| --- | --- | --- |
| Fact | The spec, the code, the product docs, a cited source | Plain text, with a link where possible |
| Assumption | Your guess where information was missing | **[Assumption]** before it |
| Proposal | Your idea for something better | Under **UX proposal** |

Never present an assumption or a proposal as a requirement.
