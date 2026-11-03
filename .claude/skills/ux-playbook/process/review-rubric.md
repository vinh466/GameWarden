# Review rubric

Use it to self-review a tier M or L change, and as a reviewer of any UI pull request. Mark each line **pass**,
**fail** or **n/a**. A failed **blocker** (B) must be fixed before merge. A failed **major** (M) needs a fix or a
written reason. A failed **minor** (m) may become a follow-up.

Check on the screenshots from the [visual check](visual-check.md) and in the code.

## Task and hierarchy

- (B) The screen's main job is clear, and its main action is visible without scrolling at desktop width.
- (M) There is one primary action per view. The other actions are visibly secondary. ([actions](../decisions/actions.md))
- (M) The heading order is logical: one H1, then H2s for sections.
- (m) Related things are grouped. Spacing uses theme tokens and follows the screen's rhythm. ([layout](../decisions/layout.md))

## States

- (B) Every data view has a loading, an empty and an error state. ([loading](../patterns/loading.md), [empty states](../patterns/empty-states.md))
- (B) Every action shows a pending state and a result. A failure says what happened and how to recover. ([feedback](../decisions/feedback.md))
- (M) A disabled action says why it is disabled.
- (M) A partial failure or a lost connection keeps the working parts usable. ([degraded experiences](../patterns/degraded-experiences.md))

## Safety and data

- (B) A destructive action is confirmed, or can be undone, according to its severity. ([destructive actions](../flows/destructive-actions.md))
- (B) The user's input survives a validation error and a failed request.
- (B) No secret is pre-filled, logged, cached or shown after it is saved.
- (M) Leaving with unsaved changes warns the user, when the form is long enough to matter. ([saving](../patterns/saving.md))

## Forms and input

- (B) Every input has a visible label. A placeholder is never the label. ([forms](../patterns/forms.md))
- (M) Validation errors appear next to the field, say how to fix them, and focus moves to the first one on submit.
- (M) The choice component fits the choice: count, single or multiple, immediate or on submit. ([selection](../decisions/selection.md))

## Discoverability and flow

- (M) A first-time user can tell what the screen is for and what to do first. ([feature onboarding](../patterns/feature-onboarding.md))
- (M) Advanced options sit behind a clearly labelled control, and nothing the main task needs is hidden. ([progressive disclosure](../patterns/progressive-disclosure.md))
- (M) Navigation shows where the user is and how to go back. ([navigation](../decisions/navigation.md))
- (m) The flow has no steps the task does not need.

## Accessibility

- (B) Every action can be reached and used by keyboard, and focus is visible. ([accessibility](../accessibility.md))
- (B) An icon-only control has an accessible name.
- (B) Meaning is never carried by colour alone.
- (M) Dialogs trap focus and return it when they close. Status changes are announced.

## Responsive and themes

- (B) Nothing overflows or overlaps at 390 px wide. Touch targets are big enough.
- (M) Light and dark schemes both read well. No hard-coded colours.
- (m) Text survives 200 % zoom and long translations.

## Words

- (M) Buttons say the action ("Delete server"). Errors say what happened and what to do. ([writing](../writing.md))
- (m) Terms match the rest of the product. Sentence case throughout.

## Consistency

- (M) The change uses the product's shared components and Mantine before custom code.
- (M) A new pattern or component came with a [UX proposal](ux-challenge.md).

## Result line

End the review with one line, for example:
`Rubric: 2 blockers fixed, 0 open; majors: 1 with reason (no unsaved-changes guard: one-field form); minors: 0.`
