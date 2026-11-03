# Actions: choosing the component

> **Use when:** you add or change something the user clicks to make a thing happen: buttons, icon buttons, a "more" menu, a toolbar, dialog and form footers, copy buttons, or an action that is unavailable or pending.
> **Sources:** Primer [Button guidelines](https://primer.style/product/components/button/guidelines/), [Button accessibility](https://primer.style/product/components/button/accessibility/), [IconButton guidelines](https://primer.style/product/components/icon-button/guidelines/), [IconButton accessibility](https://primer.style/product/components/icon-button/accessibility/), [ButtonGroup guidelines](https://primer.style/product/components/button-group/guidelines/), [ActionMenu guidelines](https://primer.style/product/components/action-menu/guidelines/), [ActionBar guidelines](https://primer.style/product/components/action-bar/guidelines/), [Dialog guidelines](https://primer.style/product/components/dialog/guidelines/), [Links and buttons](https://primer.style/accessibility/design-guidance/links-and-buttons/) · Mantine [Button](https://mantine.dev/llms/core-button.md), [ActionIcon](https://mantine.dev/llms/core-action-icon.md), [Menu](https://mantine.dev/llms/core-menu.md), [Tooltip](https://mantine.dev/llms/core-tooltip.md), [CopyButton](https://mantine.dev/llms/core-copy-button.md), [VisuallyHidden](https://mantine.dev/llms/core-visually-hidden.md)

## Decide

| Situation | Use | Not | Why |
| --- | --- | --- | --- |
| The main action of a page, dialog or form | `Button` (`variant="filled"`, the default) | Several filled buttons | One clear next step per view |
| Other actions beside it | `Button variant="default"` | A second filled button | Quieter, still clearly a button |
| Low-emphasis actions (Cancel in a toolbar, "Show more") | `variant="subtle"` or `variant="light"` | `Anchor` | It acts, so it stays a button |
| A destructive action on the page (Delete, Revoke) | `Button variant="light" color="red"` or `variant="outline" color="red"` | Filled red on the page | Filled red is kept for the final confirm: [destructive actions](../flows/destructive-actions.md) |
| An action with a well-known icon, in a toolbar or a row | `ActionIcon` + `aria-label` + `Tooltip` | An icon with no name | Screen readers and voice control need a name |
| Rare or secondary actions (more than two extra, or seldom used) | A "More" `ActionIcon` (`IconDots`) opening a `Menu` | A row of five buttons | Keeps the surface calm; still one click away |
| A main action with variants (Export as CSV / JSON) | `Button.Group`: the action + an `ActionIcon` opening a `Menu` | Two separate primaries | One split button reads as one action |
| Choosing a value or view (List / Board) | `SegmentedControl`: see [selection](selection.md) | `Button.Group` with an "active" style | Buttons act; they do not hold a selected state |
| Going somewhere | `Anchor`, or `Button component="a"`: see [navigation](navigation.md) | `Button` with `onClick={navigate}` | Links support new tab, copy URL, back button |
| Copying a value (token, URL, ID) | `CopyButton` with an `ActionIcon` or `Button` | A custom clipboard handler | Built-in `copied` state for feedback |
| Work is running after a click | The same button with `loading` | A spinner somewhere else | Feedback where the user looks |
| The action is unavailable now, and the user can change that | Keep it visible; explain why (rule 7) | Hiding it; `disabled` with no reason | Users cannot use what they cannot find |
| The action is never available to this user (no permission) | Hide it, or show a read-only notice: [degraded experiences](../patterns/degraded-experiences.md) | A disabled button forever | Nothing the user can do about it |

## Rules

1. One primary (`filled`) button per view: page header, dialog, form, card. Everything else is `default`, `light` or
   `subtle`. Do not let colour alone tell buttons apart: the label must say what each does.
2. Footers follow where the form lives. **Dialog** (and a reply box under a text area): right-align the buttons
   (`Group justify="flex-end"`), primary **last**, Cancel to its left. **Form on a page**: put the buttons at the
   bottom left, in line with the fields, primary **first**, Cancel to its right. In a page header the primary is the
   rightmost action.
3. Label every button verb + object, sentence case, one to three words, never wrapping: "Create server",
   "Save changes". Never "OK", "Yes", "Submit". Repeated buttons get unique names ("Edit billing address").
   See [writing](../writing.md).
4. Destructive actions use `color="red"` and say what they destroy ("Delete server"). Keep them away from the
   primary action, or put them last in a "More" menu after a `Menu.Divider`. Confirm or offer undo:
   [destructive actions](../flows/destructive-actions.md).
5. Show pending work with `loading` on the button that started it. Keep the label. Mantine disables a loading button,
   which blocks double submits. Announce the result: see [feedback](feedback.md).
6. Never disable a form's submit button. Keep it enabled, validate on submit and show field errors:
   [forms](../patterns/forms.md).
7. An unavailable action says why. Prefer explanatory text next to it. If it must look disabled, use `data-disabled`
   and `aria-disabled="true"` (not `disabled`), make `onClick` call `event.preventDefault()` and do nothing else,
   and add a `Tooltip` with the reason and focus events. A tooltip does not open on touch, so on screens used on
   phones show the reason as text too. Disable an action only when the user can fix the cause right there.
8. Never put a `Tooltip` on a `disabled` button: disabled elements fire no `mouseleave` (the tooltip gets stuck) and
   cannot take focus (keyboard users never see it).
9. Every `ActionIcon` has an `aria-label` (verb + object) and a `Tooltip` with the same words and
   `events={{ hover: true, focus: true, touch: false }}`. Use only icons whose meaning is obvious; otherwise use a
   text `Button`.
10. A "More" menu holds actions only: no inputs, checkboxes or search (for choices, see [selection](selection.md)).
    Name its trigger with the object ("More actions for web-01"). Give all items icons or none. Use the default
    click trigger, never `trigger="hover"`.
11. A toolbar of icon actions uses `ActionIcon variant="subtle"`, the same content type throughout (all icons or all
    text), and no primary button.
12. Use `Button.Group` only for tightly related actions. Do not wrap its children in other elements.
13. After a copy, show the result in place: the `CopyButton` `copied` state swaps the icon and tooltip text
    ("Copied") for about two seconds (`timeout={2000}`).
14. Make targets at least 24 × 24 px; on phones avoid `size="xs"` for main actions.

## Recipes

### Footers: dialog vs form on a page

```tsx
import { Button, Group } from '@mantine/core';

// Dialog footer: right-aligned, primary last.
<Group justify="flex-end" gap="sm" mt="lg">
  <Button variant="default" onClick={onCancel}>Cancel</Button>
  <Button type="submit" loading={saving}>Save changes</Button>
</Group>

// Form on a page: bottom left, in line with the fields, primary first.
<Group gap="sm" mt="lg">
  <Button type="submit" loading={saving}>Save changes</Button>
  <Button variant="default" onClick={onCancel}>Cancel</Button>
</Group>
```

### Icon button with a name

```tsx
import { ActionIcon, Tooltip } from '@mantine/core';
import { IconRefresh } from '@tabler/icons-react';

<Tooltip label="Restart web-01" events={{ hover: true, focus: true, touch: false }}>
  <ActionIcon variant="subtle" aria-label="Restart web-01" onClick={restart}>
    <IconRefresh size={18} />
  </ActionIcon>
</Tooltip>
```

### Unavailable, with the reason

```tsx
import { Button, Tooltip } from '@mantine/core';

<Tooltip label="Stop the server before you restore a backup" events={{ hover: true, focus: true, touch: false }}>
  <Button data-disabled aria-disabled="true" onClick={(event) => event.preventDefault()}>
    Restore backup
  </Button>
</Tooltip>
```

### "More" menu with a destructive item last

```tsx
import { ActionIcon, Menu } from '@mantine/core';
import { IconCopy, IconDots, IconPencil, IconTrash } from '@tabler/icons-react';

<Menu position="bottom-end">
  <Menu.Target>
    <ActionIcon variant="subtle" aria-label="More actions for web-01"><IconDots size={18} /></ActionIcon>
  </Menu.Target>
  <Menu.Dropdown>
    <Menu.Item leftSection={<IconPencil size={14} />} onClick={rename}>Rename</Menu.Item>
    <Menu.Item leftSection={<IconCopy size={14} />} onClick={duplicate}>Duplicate</Menu.Item>
    <Menu.Divider />
    <Menu.Item color="red" leftSection={<IconTrash size={14} />} onClick={confirmDelete}>Delete server</Menu.Item>
  </Menu.Dropdown>
</Menu>
```

### Copy with feedback

```tsx
import { ActionIcon, CopyButton, Tooltip } from '@mantine/core';
import { IconCheck, IconCopy } from '@tabler/icons-react';

<CopyButton value={token} timeout={2000}>
  {({ copied, copy }) => (
    <Tooltip label={copied ? 'Copied' : 'Copy token'} events={{ hover: true, focus: true, touch: false }}>
      <ActionIcon variant="subtle" color={copied ? 'teal' : 'gray'} aria-label="Copy token" onClick={copy}>
        {copied ? <IconCheck size={16} /> : <IconCopy size={16} />}
      </ActionIcon>
    </Tooltip>
  )}
</CopyButton>
```

## Pitfalls

- Three filled buttons in one header. Fix: one primary; the rest `default` or in a "More" menu.
- "OK" / "Cancel" footers. Fix: a verb + object on the primary ("Create server").
- `<Tooltip><Button disabled>` that never shows, or stays stuck. Fix: the `data-disabled` recipe above.
- A disabled Save button until the form is valid; the user cannot learn what is wrong. Fix: enabled + field errors.
- An `ActionIcon` with only a tooltip and no `aria-label`. Fix: both, with the same words.
- Five "Delete" buttons in a list, all with the same name. Fix: add the object: "Delete web-01".
- A button that calls `navigate()`. Fix: a link (`Anchor` or `Button component="a"`).
- A Mantine `Tooltip` without focus events, so keyboard users never see it. Fix: set `events`, or set it once in the
  theme's `components.Tooltip.defaultProps`.

## Checklist

- [ ] Each view has at most one filled button, and it is the main action.
- [ ] Dialog footers are right-aligned with the primary last; page forms put the primary first, bottom left.
- [ ] Every label is verb + object; no "OK", "Yes", "Submit"; repeated labels name their object.
- [ ] Destructive actions are red, named, and confirmed or undoable.
- [ ] Every `ActionIcon` has an `aria-label` and a `Tooltip` that opens on keyboard focus.
- [ ] Pending actions show `loading` on the button that started them.
- [ ] No visible action is disabled without a reason the user can read; no `Tooltip` wraps a `disabled` button.
- [ ] Submit buttons are never disabled.
- [ ] Rare actions live in a "More" `Menu`; the menu holds only actions.
