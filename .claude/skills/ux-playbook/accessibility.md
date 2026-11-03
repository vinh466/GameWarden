# Accessibility

> **Use when:** you build or review anything a user reads or operates: structure, labels, keyboard, focus, colour, motion, zoom, images.
> **Sources:** Primer [Accessibility fundamentals](https://primer.style/accessibility/foundations/accessibility-fundamentals/), [Semantic HTML and ARIA](https://primer.style/accessibility/design-guidance/semantic-html-and-aria/), [Links and buttons](https://primer.style/accessibility/design-guidance/links-and-buttons/), [Focus management](https://primer.style/accessibility/design-guidance/focus-management/), [App-like web experiences](https://primer.style/accessibility/design-guidance/app-like-web-experiences/), [Color considerations](https://primer.style/accessibility/design-guidance/color-considerations/), [Motion and animation](https://primer.style/accessibility/design-guidance/motion-and-animation/), [Text resize and spacing](https://primer.style/accessibility/design-guidance/text-resize-and-spacing/), [Alternative text for images](https://primer.style/accessibility/design-guidance/alternative-text-for-images/), [Responsive](https://primer.style/product/getting-started/foundations/responsive/) · Mantine [Are Mantine components accessible?](https://mantine.dev/llms/q-are-mantine-components-accessible.md), [AppShell](https://mantine.dev/llms/core-app-shell.md), [ActionIcon](https://mantine.dev/llms/core-action-icon.md), [VisuallyHidden](https://mantine.dev/llms/core-visually-hidden.md), [Modal](https://mantine.dev/llms/core-modal.md), [FocusTrap](https://mantine.dev/llms/core-focus-trap.md), [Tooltip](https://mantine.dev/llms/core-tooltip.md), [Notification](https://mantine.dev/llms/core-notification.md), [Theme object](https://mantine.dev/llms/theming-theme-object.md), [useReducedMotion](https://mantine.dev/llms/hooks-use-reduced-motion.md)

Target: WCAG 2.2 level AA. Mantine components ship with roles, ARIA attributes and keyboard support, but the
structure, names, order and words are yours.

## Mantine does it vs you must do it

| Mantine already does | You still must |
| --- | --- |
| Roles and `aria-*` on its widgets (Menu, Tabs, Combobox, Modal) | Pick the right element: a link navigates, a button acts |
| Keyboard support inside each widget (arrows in Menu, Esc in Modal) | Make every action of the page reachable, in a logical order |
| Focus trap and focus return in `Modal` and `Drawer` | Give `Modal` a `title`; decide where focus goes after delete or navigation |
| A focus ring on buttons and inputs (`theme.focusRing: 'auto'`) | Never set `focusRing: 'never'`; check the ring is visible on your colours |
| Label, description and error wired to the input (`TextInput label`, `error`) | Write a visible `label` on every input; `aria-label` on every `ActionIcon` |
| `role="alert"` on `Notification` by default | Announce status changes that do not use a notification |
| Reduced motion, only if you enable it | Set `respectReducedMotion: true` in the theme |

## Semantics and landmarks

- One `h1` per page: the page title. Headings go down one level at a time (`h2` under `h1`, never `h1` then `h4`).
- Choose the heading level by structure. Change its look with `Title order={2} size="h4"`, not by picking another level.
- Build the frame with `AppShell`: `AppShell.Header`, `AppShell.Navbar` and `AppShell.Main` render `header`, `nav`
  and `main`. Put the page content inside `AppShell.Main`.
- Give a page with several `nav` regions an `aria-label` on each ("Main", "Settings sections").
- A list of things is a real list (`List`, or `ul`/`ol`). Tabular data is a real `Table` with header cells.
- Never put `onClick` on a `div` or `Text`. Use `Button`, `UnstyledButton` or `Anchor`.
- Use ARIA only where no native element or Mantine component fits. Never `aria-label` a non-interactive `div`.
- Update `document.title` on every route change so it names the view ("Server settings · Acme").
- Hide decorative icons and avatars from screen readers (`aria-hidden`) when text next to them says the same thing.

## Links vs buttons

| The control … | Use | Never |
| --- | --- | --- |
| Goes to another page, view or anchor, or downloads a file | `Anchor` or a router link (`<a href>`) | `Button` with `navigate()` |
| Saves, deletes, opens a dialog, expands, submits | `Button` / `ActionIcon` (`<button>`) | `<a href="#">`, `role="link"` |

- A link that must look like a button: `Button component="a" href="…"` (or the router's link as `component`).
- Do not open links in a new tab by default. If it is essential (it would lose unsaved work), say so inside the link:
  "Pricing (opens in a new tab)".
- See [actions](decisions/actions.md) and [navigation](decisions/navigation.md) for which button or link to pick.

## Labels and names

- Every input has a visible `label`. A placeholder is an example, never the label: it vanishes, has low contrast and
  is not read as a name. See [forms](patterns/forms.md).
- Every `ActionIcon` has an `aria-label` that names the action and its object: `aria-label="Delete server web-01"`.
- Name by verb + object, and make repeated controls unique ("Edit billing address", not five "Edit").
- Do not over-describe: no "button", "link", "click to" or "icon" in a name. Assistive tech says the role.
- When the visible text and the accessible name differ, the name starts with the visible text (voice control users
  say what they see).
- Prefer visible text, then `VisuallyHidden` text, then `aria-labelledby`, then `aria-label`.
- A field group (radio, checkbox) has a group label (`Radio.Group label`, `Checkbox.Group label`).

## Keyboard

- Every action works with the keyboard alone: Tab and Shift+Tab between controls, arrows inside a widget
  (menu, tabs, radio group), Enter and Space to activate, Esc to close an overlay.
- Tab order follows the visual order. Never use a positive `tabIndex`.
- No keyboard traps. Only a modal overlay holds focus, and Esc or a close button releases it.
- Every hover or swipe action has a keyboard path. A row action shown only on hover must also show on focus.
- Keep the focus ring visible: leave `theme.focusRing` at `'auto'` (ring for keyboard users) or set `'always'`.
  If you replace it with `focusClassName`, the replacement must contrast 3:1 with its background.
- Do not make keyboard shortcuts the only way to do something.

## Focus management

- Dialogs: Mantine `Modal` and `Drawer` trap focus and return it to the trigger on close (`trapFocus`, `returnFocus`
  are on). Do not turn these off. Put `data-autofocus` on the element that should get focus first; for a
  destructive dialog that is the Cancel button or the input, not the destructive button.
- After client-side navigation, move focus to the new view's `h1` (give it `tabIndex={-1}` and call `focus()`).
- After "Load more", move focus to the first new item.
- After deleting an item, move focus to a logical neighbour: the next item, else the previous, else the list's
  heading or its "Add" button. Never let focus fall back to `body`.
- After applying a filter, keep focus on the filter control. Do not jump to the results.
- Never move focus on your own (on load, on a timer, on a background update) except in the cases above.

## Announcements

- A status change the user did not move focus to must be announced: saved, deleted, failed, "3 results".
- `Notification` renders with `role="alert"` by default, so a notification is announced. Keep its text short.
- For quiet updates (a result count, "Saving…"), render a polite live region that stays mounted and change only its
  text: `<VisuallyHidden role="status">{message}</VisuallyHidden>`.
- Do not announce every keystroke or every poll. Announce the result, once.
- Errors on fields: set the input's `error` prop (it also sets `aria-invalid`). On submit, move focus to the first
  invalid field.

## Colour and contrast

- Text contrast at least 4.5:1 (3:1 for large text). Icons that carry meaning, input borders and focus rings at
  least 3:1. Check both light and dark schemes.
- Never use colour alone. Pair every status colour with an icon or a word: a red `Badge` says "Failed"; an error
  field shows a message, not only a red border.
- Use theme colours (`c="dimmed"`, `color="red"`); do not invent lighter greys for secondary text.
- Charts: label series directly or add patterns; do not rely on a legend of colour swatches alone.
- Disabled controls may be low contrast, so never put the only explanation inside a disabled control.

## Motion

- Enable reduced motion in the theme: `createTheme({ respectReducedMotion: true })` (default is `false`).
- For your own animations, read `useReducedMotion()` from `@mantine/hooks` and drop the transition when it is `true`.
- Use motion to explain a change (an item appears, a panel opens), not to decorate.
- Anything that moves for more than 5 seconds needs a pause control or must not start on its own.
- Never flash more than three times a second. Never carry information only in an animation.

## Text resize, spacing and touch

- At 200 % browser zoom, and at 320 px width, no text is cut off, overlaps or hides a control.
- Do not set fixed heights on containers that hold text. Let them grow; use `lineClamp` only with a way to read
  the full text.
- Use theme sizes (rem-based); never fixed `px` font sizes.
- Touch targets at least 24 by 24 px; prefer the `md` or larger `Button`/`ActionIcon` size on touch layouts.

## Images

- Every `Image` (and `img`) has an `alt`. Informative: say what it shows, briefly. Decorative: `alt=""`.
- No "image of" or "picture of" in alt text. Text inside an image goes into the alt.
- An icon-only control gets its name from `aria-label`, not from the icon.

## Tooltips

- A tooltip adds a hint. It is never the only place for information the user needs: touch devices have no hover.
- Mantine `Tooltip` opens on hover only by default. Add `events={{ hover: true, focus: true, touch: false }}` so
  keyboard users see it too.
- Never put interactive content (links, buttons) inside a `Tooltip`. Use a `Popover`; see [overlays](decisions/overlays.md).
- A tooltip on an `ActionIcon` does not replace its `aria-label`.

## Recipe

```tsx
import { ActionIcon, Group, Title, Tooltip, VisuallyHidden } from '@mantine/core';
import { IconTrash } from '@tabler/icons-react';

export function ServerHeader({ status }: { status: string }) {
  return (
    <Group justify="space-between">
      <Title order={1}>web-01</Title>
      <Tooltip label="Delete server" events={{ hover: true, focus: true, touch: false }}>
        <ActionIcon variant="subtle" color="red" aria-label="Delete server web-01">
          <IconTrash size={18} aria-hidden />
        </ActionIcon>
      </Tooltip>
      <VisuallyHidden role="status">{status}</VisuallyHidden>
    </Group>
  );
}
```

## Checklist

- [ ] One `h1`; headings never skip a level; the frame uses `AppShell` regions (`header`, `nav`, `main`).
- [ ] Links navigate and buttons act; no clickable `div` or `Text`.
- [ ] Every input has a visible label; no placeholder used as a label.
- [ ] Every `ActionIcon` has an `aria-label` naming action and object; repeated controls have unique names.
- [ ] The whole flow works with keyboard only, in visual order, with a visible focus ring at every step.
- [ ] Dialogs trap and return focus; after delete or navigation, focus lands on a logical element, never `body`.
- [ ] Status changes (saved, deleted, failed, result counts) are announced.
- [ ] Text contrast ≥ 4.5:1 and meaningful icons ≥ 3:1 in light and dark; no meaning by colour alone.
- [ ] `respectReducedMotion: true` in the theme; custom animations check `useReducedMotion()`.
- [ ] At 200 % zoom and 320 px width nothing is clipped or overlapping.
- [ ] Every image has `alt` (empty for decorative).
- [ ] No information or action reachable only through a tooltip or hover.
