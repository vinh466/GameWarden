---
name: ux-playbook
description: >-
  UX knowledge for building and reviewing user interfaces in React + Mantine web apps, distilled from GitHub's Primer
  design guidance. Use it whenever a task adds or changes something a user sees or does: a page, dialog, form, table,
  navigation, button, message, or the empty, loading and error states around them; when planning a feature's flow;
  and when reviewing UI in a pull request. It tells you which Mantine component fits which situation, which states a
  screen needs, how to lay out and word it, how to check it on screenshots, and how to propose a better flow or
  component to the product owner without changing business rules.
license: MIT
---

# UX Playbook

Build UI that is **correct for the business, easy to use, and consistent**. Mantine gives the components; this
playbook tells you **when** to use which, **which states** a screen needs, and **how to check** the result.
Read only the pages your task needs.

## Precedence

When sources disagree, the higher one wins:

1. **Business requirements**: the spec, ticket or owner's instruction. Never change one silently.
2. **The product's own rules**: `AGENTS.md` / `CLAUDE.md`, its UX and design docs, its existing components.
3. **This playbook.**
4. **Mantine's docs and defaults.**

If 2 and 3 disagree, follow 2. If you think 2 or 1 is wrong, follow it anyway and raise a
[UX proposal](process/ux-challenge.md).

Before UI work, look for the product's UX rules (search the repo docs for "UX", "UI", "design", "panel"). If there
are none, draft them with the [product brief template](templates/product-brief.md) and propose the draft in the plan
or PR, with every guess labelled **[Assumption]**.

## Pick the tier

| Tier | The change | What you do |
| --- | --- | --- |
| **S** | Small change on an existing screen: a field, a button, a message, a column | Read the page for the component you touch. Run its checklist. Take screenshots if the layout changed. |
| **M** | A new screen, dialog or form, or a large change to one | Write a short [UX spec](templates/ux-spec.md) first. Build it. Do the [visual check](process/visual-check.md). Self-review with the [rubric](process/review-rubric.md). |
| **L** | A new flow across screens, a new feature area, or a change to navigation | Do a [research note](templates/research-note.md) with 2–3 options. Build the safe option, A. Then treat each screen as tier M. |

Details and deliverables for each tier: [workflow](process/workflow.md).

## Find the page

| You are deciding … | Read |
| --- | --- |
| Page structure, hierarchy, spacing, responsive layout | [decisions/layout](decisions/layout.md) |
| Buttons, primary vs secondary, icon buttons, action menus | [decisions/actions](decisions/actions.md) |
| Modal vs Drawer vs Popover vs inline, tooltips | [decisions/overlays](decisions/overlays.md) |
| App navigation, tabs, breadcrumbs, steppers, pagination, link vs button | [decisions/navigation](decisions/navigation.md) |
| Tables, lists, cards, status labels, dates, long text | [decisions/data-display](decisions/data-display.md) |
| Toasts, alerts, banners, inline messages | [decisions/feedback](decisions/feedback.md) |
| Which input for a choice: checkbox, switch, radio, segmented control, select, combobox, chips | [decisions/selection](decisions/selection.md) |
| Forms, labels, validation, required fields | [patterns/forms](patterns/forms.md) |
| Save buttons, autosave, unsaved changes | [patterns/saving](patterns/saving.md) |
| Nothing to show yet, no results | [patterns/empty-states](patterns/empty-states.md) |
| Waiting for data or for work to finish | [patterns/loading](patterns/loading.md) |
| Advanced options, "show more", details on demand | [patterns/progressive-disclosure](patterns/progressive-disclosure.md) |
| Introducing a feature, first run, hints | [patterns/feature-onboarding](patterns/feature-onboarding.md) |
| Backend down, offline, partial failure, missing permission | [patterns/degraded-experiences](patterns/degraded-experiences.md) |
| Delete, reset, revoke, overwrite | [flows/destructive-actions](flows/destructive-actions.md) |
| Labels, button text, error text, empty-state text | [writing](writing.md) |
| Keyboard, focus, labels, contrast, ARIA | [accessibility](accessibility.md) |
| Looking up a Mantine API, theme tokens, versions | [mantine](mantine.md) |
| The reason behind a rule | [principles](principles.md) |

## Always

- Use the product's existing components and Mantine before writing your own. Use one component per job.
- Give every data view its **loading, empty and error** states. Give every action a **pending state and a result**.
- Keep **one primary action** per view. Secondary actions are quieter. Rare actions go in a menu.
- An action that is unavailable right now **stays visible and says why**, in text the user can reach by keyboard and
  touch, not only in a tooltip. Prefer an inactive look (`data-disabled`) to the `disabled` attribute, and never
  disable a submit button: [actions](decisions/actions.md).
- Confirm or allow undo for anything that destroys data. Never lose the user's input on an error.
- Every input has a visible label. Meaning is never carried by colour alone. Everything is reachable by keyboard.
- Check the result at phone width and desktop width, in light and dark: [visual check](process/visual-check.md).
- Examples here use literal English strings. Use the product's i18n instead.

## When you think it could be better

You may, and should, propose better flows and components. The rule is **build the safe default, attach the proposal**:

- Build what the spec asks for, using the patterns here.
- Add your idea as a **UX proposal** in the plan or PR, in the [fixed format](process/ux-challenge.md).
- Stop and ask **before** building only when the spec would lose data, expose a secret, block a user who relies on a
  keyboard or screen reader, or contradicts itself.

## Report

Close every UI task with a **UX notes** section in the PR or report:

```markdown
## UX notes
- Tier: M — new "Mods" tab
- States: loading ✓ empty ✓ error ✓ disabled-with-reason ✓
- Screenshots: 1280 light/dark, 390 light/dark (attached)
- Rubric: all pass; n/a: destructive (none)
- Assumptions: [Assumption] mods list is short (< 50), so no pagination
- UX proposals: 1 (see below)
```
