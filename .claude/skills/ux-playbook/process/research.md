# Research

For tier L work, and for any UX proposal that needs evidence. The goal is to learn how a problem is usually solved
**before** inventing a solution, then offer the owner real options.

## Time box

Keep it short: a few sources, one page of notes. Research answers a design question. It is not a literature review.
If you have no web access, use this playbook and the product's own screens, and say so.

## Where to look, in order

1. **The product itself.** How do its other screens solve a similar problem? Consistency beats a better pattern
   used once.
2. **This playbook.** The matching pattern or flow page.
3. **Primer** ([primer.style](https://primer.style/product/ui-patterns/)). Its UI patterns, component usage
   guidance and accessibility pages explain *why*, not only *how*.
4. **Mantine** ([mantine.dev/llms.txt](https://mantine.dev/llms.txt), [ui.mantine.dev](https://ui.mantine.dev)).
   What the library already offers, so a proposal stays buildable.
5. **Other mature design systems**, for the same component or pattern. [Open UI](https://open-ui.org) compares
   components across systems, and the [GOV.UK Design System](https://design-system.service.gov.uk) is strong on
   forms and errors, backed by user research.
6. **Comparable products** your users already know. Name the product and the screen; describe it, don't copy it.

## What to extract

- The **user problem** each source solves, in one line. Make sure it is the same problem as yours.
- The **pattern**: its anatomy, its states, its rules.
- The **trade-offs**: what it costs the user (steps, attention) and the team (components, API).
- **Evidence**, when a source has it (usability findings, accessibility requirements). Mark opinion as opinion.

## Turn it into options

Write the [research note](../templates/research-note.md):

- **Option A** is safe: built from existing patterns, low risk. You build this one by default.
- **Option B** and **option C** are bolder, and are allowed to be creative. Each says what it improves for the user
  and what it costs.
- Recommend one option, with the reason. The owner decides. You never change a business rule to make an option work.
