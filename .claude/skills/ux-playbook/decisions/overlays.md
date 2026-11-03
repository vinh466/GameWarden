# Overlays: choosing the component

> **Use when:** content or a task should appear above the page, and you are choosing between a `Modal`, a `Drawer`, a `Popover`, a `HoverCard`, a `Tooltip`, a `Menu`, inline expansion or a new page.
> **Sources:** Primer [Dialog guidelines](https://primer.style/product/components/dialog/guidelines/), [Dialog accessibility](https://primer.style/product/components/dialog/accessibility/), [ConfirmationDialog guidelines](https://primer.style/product/components/confirmation-dialog/guidelines/), [Tooltip guidelines](https://primer.style/product/components/tooltip/guidelines/), [Tooltip accessibility](https://primer.style/product/components/tooltip/accessibility/), [Popover guidelines](https://primer.style/product/components/popover/guidelines/), [Popover accessibility](https://primer.style/product/components/popover/accessibility/), [Responsive](https://primer.style/product/getting-started/foundations/responsive/) · Mantine [Modal](https://mantine.dev/llms/core-modal.md), [Drawer](https://mantine.dev/llms/core-drawer.md), [Popover](https://mantine.dev/llms/core-popover.md), [HoverCard](https://mantine.dev/llms/core-hover-card.md), [Tooltip](https://mantine.dev/llms/core-tooltip.md), [Menu](https://mantine.dev/llms/core-menu.md), [Modals manager](https://mantine.dev/llms/x-modals.md), [useMediaQuery](https://mantine.dev/llms/hooks-use-media-query.md)

## Decide

Start from "no overlay". Use one only when the content is a separate, short task or a small addition to a control.

| Situation | Use | Not | Why |
| --- | --- | --- | --- |
| A short, focused task the user must finish or cancel before going on (rename, invite, small create form) | `Modal` | A new page; a `Popover` | Blocks the page, keeps the user in place, one clear exit |
| Confirm a destructive or irreversible action | `modals.openConfirmModal` (`@mantine/modals`): see [destructive actions](../flows/destructive-actions.md) | A hand-made modal per action | One consistent confirmation |
| Look at one item's details or settings while the list stays in view (preview, quick edit of a few fields) | `Drawer position="right"` | A `Modal` | The list behind stays recognisable |
| A long form, several steps, its own navigation, or content people link to | A page (route) | `Modal`, `Drawer` | Room, a URL, the back button, no lost work on a stray Esc |
| Small, non-blocking content tied to one control (two fields, a filter, a short explanation) | `Popover` | `Modal` | Light, close to its trigger, the page stays usable |
| A preview of a linked thing on hover (a user, a server) | `HoverCard` on the link | `Tooltip` | Rich content; the link still leads to the full page |
| The name of an icon button, or a short hint on a control | `Tooltip` | `Popover`, `HoverCard` | Text only, appears on hover and focus |
| A list of actions | `Menu`: see [actions](actions.md) | A `Popover` of buttons | Menu roles and arrow-key navigation |
| A few extra fields or details within the flow ("Advanced options", "Show details") | Inline `Collapse`: see [progressive disclosure](../patterns/progressive-disclosure.md) | Any overlay | Stays in context and in reading order |
| Global navigation on phones | `AppShell` navbar behind a `Burger`: see [navigation](navigation.md) | A hand-made left `Drawer` | `AppShell` already does it |

## Rules

1. One overlay at a time. Do not open a modal from a modal. If a step inside a modal needs confirmation, show it
   inside the same modal, or use `Modal.Stack` with `useModalsStack` so Esc closes only the top one. Never deeper
   than two.
2. Give every `Modal` and `Drawer` a `title` that names the task ("Rename server"); it becomes the accessible name.
   Never "Are you sure?", "Dialog" or no title. Keep the close button.
3. Keep a modal to one column and one task. Put the actions in a footer, right-aligned, primary last:
   see [actions](actions.md). If it needs tabs, a side menu or scrolling through many sections, make it a page.
4. Size: `size="sm"` for confirmations, the default `"md"` for short forms, `"lg"` at most. Larger means a page.
5. On phones, show a modal that has inputs full screen: `fullScreen={isMobile}` with
   `transitionProps={{ transition: 'fade' }}`. Get `isMobile` from `useMediaQuery` with the theme's `sm` breakpoint.
6. Leave Mantine's focus handling on: `trapFocus`, `returnFocus` and `closeOnEscape` stay `true`. Put
   `data-autofocus` on the first input. In a destructive dialog, focus Cancel, not the destructive button. If the
   trigger is gone after closing (a deleted row), move focus yourself: [accessibility](../accessibility.md).
7. Protect input. A modal or drawer with a form that has changes sets `closeOnClickOutside={false}`, and Esc or the
   close button asks before discarding: see [saving](../patterns/saving.md).
8. When the action inside fails, keep the overlay open with an `Alert` at the top of its body:
   see [feedback](feedback.md).
9. `Drawer` is modal too: the page behind is blocked. Use `position="right"` for details, previews and settings of
   the current page. Do not put long create or edit flows in a drawer; use a page.
10. `Popover`: small, non-critical content (never an error), dismissed by Esc, an outside click or a visible control.
    Set `trapFocus` when it holds inputs. Its target must be a `Button` or `ActionIcon` so Enter and Space work.
11. `Tooltip`: short plain text that adds to a control. Never essential information, never links or buttons inside,
    never on a non-interactive element (`Text`, `Badge`, `div`), never on a `disabled` button. Always set
    `events={{ hover: true, focus: true, touch: false }}`. Touch screens have no hover, so the information must also
    exist somewhere else.
12. `HoverCard`: only content that is also available elsewhere (usually the page the target links to). Keyboard users
    can open it but cannot move into it, so never put the only copy of a link or button inside.
13. Open overlays on click or focus, never on hover alone, and never on page load unless the user asked for it.
14. Content people need to share or bookmark belongs on a page. If a modal must be reachable by URL, drive `opened`
    from a URL parameter, render the page it belongs to behind it, and remove the parameter on close.

## Recipes

### Modal form, full screen on phones

```tsx
import { Button, Group, Modal, Stack, TextInput, useMantineTheme } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';

type Props = { opened: boolean; onClose: () => void; onSubmit: () => void };

export function RenameServer({ opened, onClose, onSubmit }: Props) {
  const theme = useMantineTheme();
  const isMobile = useMediaQuery(`(max-width: ${theme.breakpoints.sm})`);
  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title="Rename server"
      fullScreen={isMobile}
      transitionProps={{ transition: 'fade', duration: 200 }}
    >
      <form onSubmit={(event) => { event.preventDefault(); onSubmit(); }}>
        <Stack gap="md">
          <TextInput label="Server name" data-autofocus />
          <Group justify="flex-end" gap="sm">
            <Button variant="default" onClick={onClose}>Cancel</Button>
            <Button type="submit">Rename server</Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
```

### Details drawer beside a list

```tsx
import { Drawer } from '@mantine/core';

<Drawer opened={selected !== null} onClose={clearSelection} position="right" size="md" title="Server details">
  {/* read-only details and a link to the full page */}
</Drawer>
```

### Popover with a small form

```tsx
import { Button, Popover, Stack, TextInput } from '@mantine/core';

<Popover position="bottom-end" trapFocus withArrow shadow="md">
  <Popover.Target>
    <Button variant="default">Add label</Button>
  </Popover.Target>
  <Popover.Dropdown>
    <Stack gap="xs">
      <TextInput label="Label name" size="xs" />
      <Button size="xs" onClick={addLabel}>Add label</Button>
    </Stack>
  </Popover.Dropdown>
</Popover>
```

v8: `HoverCard` cannot be opened with the keyboard and is ignored by screen readers; treat it as mouse-only.

## Pitfalls

- A settings "page" built as a modal with tabs and a scroll. Fix: a route with a page header.
- A modal opens a second modal, which opens a third. Fix: one level; confirmations inside the same modal or a page.
- A long form in a modal closes on an outside click and loses the input. Fix: `closeOnClickOutside={false}` and
  ask before discarding.
- Essential help text only in a `Tooltip` on an info icon. Fix: show it as a description under the field.
- A `Tooltip` on a `Badge` or plain `Text`: keyboard users can never open it. Fix: put the text on the page.
- A `Popover` or `Menu` that opens on hover. Fix: click (the default).
- A centred modal with inputs squeezed on a 390 px phone. Fix: `fullScreen={isMobile}`.
- A modal closes on a server error and the message shows behind it. Fix: keep it open with an `Alert` inside.

## Checklist

- [ ] An inline solution or a page was considered first; the overlay type matches the decision table.
- [ ] Every `Modal` and `Drawer` has a `title` naming the task and a visible close button.
- [ ] Focus moves into the overlay, stays there, and returns to the trigger (or a logical neighbour) on close.
- [ ] Esc closes the overlay unless pending; forms with changes ask before discarding.
- [ ] No overlay opens another overlay deeper than one level.
- [ ] Modals with inputs are full screen at phone width.
- [ ] Tooltips hold only short, non-essential text, open on keyboard focus, and sit on interactive elements.
- [ ] Nothing reachable only through a `HoverCard` or `Tooltip`.
