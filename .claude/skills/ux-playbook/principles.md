# Principles

> **Use when:** you need the reason behind a rule, or a case no other page covers.
> **Sources:** Primer [UI patterns](https://primer.style/product/ui-patterns/), [Accessibility](https://primer.style/accessibility/), [Content](https://primer.style/product/getting-started/foundations/content/), [Handling new patterns](https://primer.style/product/contribute/handling-new-patterns/) · the playbook's own judgment.

Twelve principles. Every rule on the other pages applies one of them. When two principles pull apart, the lower
number wins.

## 1. Business rules are fixed; UX decides how they reach the user

Never change, drop or invent a business rule to make the UI simpler. If a rule makes the experience worse, build it
as specified and raise a [UX proposal](process/ux-challenge.md). Separate **facts** (from the spec), **assumptions**
(yours, labelled) and **proposals** (yours, optional).

## 2. Serve the task, not the screen

Every screen has one main job. The user should find its main action within a few seconds. Order everything else by
how often it is used. Put the most-used action on the surface, rarer ones in a menu, rare and risky ones deeper.
See [layout](decisions/layout.md) and [actions](decisions/actions.md).

## 3. Design every state

A screen is not done when it works with good data. It also needs **loading, empty, error, partial, disabled,
success and degraded** states. Missing states are the most common UX bug agents ship.
See [loading](patterns/loading.md), [empty states](patterns/empty-states.md) and
[degraded experiences](patterns/degraded-experiences.md).

## 4. Every action answers

Respond right away. Show a pending state while work runs. Show the result where the user is looking. When something
fails, say what happened and how to recover. See [feedback](decisions/feedback.md) and [saving](patterns/saving.md).

## 5. Protect the user's work and data

Never lose what the user typed, including after an error. Prefer **undo** to confirmation. When an action cannot be
undone, confirm it, and say exactly what will be lost. See [destructive actions](flows/destructive-actions.md).

## 6. Show the common path; disclose the rest

Show what most users need, in the order they need it. Put advanced options one deliberate step away, behind a clear
label. Never hide something the main task needs. See [progressive disclosure](patterns/progressive-disclosure.md).

## 7. Make capability discoverable

Users cannot use what they cannot find. Empty states teach the next step. A new feature is introduced where it is
used, once. An important action that is unavailable right now stays visible, looks inactive and says why, instead
of disappearing.
See [feature onboarding](patterns/feature-onboarding.md).

## 8. Consistency over novelty

Use the design system's components and the product's existing patterns first. One place decides how a thing looks
(a status, a date, an empty state). A new pattern needs a reason the old ones cannot meet. Propose it; don't
improvise it. See [Mantine](mantine.md).

## 9. Accessible by default

Use semantic elements. Give every input a label. Make every action reachable by keyboard and keep focus visible.
Never carry meaning by colour alone. Accessibility is part of the first version, not a follow-up.
See [accessibility](accessibility.md).

## 10. Words are interface

Labels name things the way users do. Buttons say what they do ("Delete server", not "OK"). Errors say what happened
and what to do next. Keep it short, in plain words. See [writing](writing.md).

## 11. Fit every screen and setting

Check at phone width and desktop width, in light and dark scheme, and at 200 % zoom. Text wraps or truncates on
purpose; nothing overflows. Touch targets are big enough for a thumb. See [visual check](process/visual-check.md).

## 12. Calm by default

Use few colours and little decoration. Use motion only to explain a change, and respect reduced motion. Interrupt
the user (a modal, a toast) only when the message deserves it. See [feedback](decisions/feedback.md).
