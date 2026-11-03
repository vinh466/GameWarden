# Progressive disclosure

> **Goal:** the user sees what most people need first and can reveal advanced options or details in one deliberate step, without losing their place.
> **Sources:** Primer [Progressive disclosure](https://primer.style/product/ui-patterns/progressive-disclosure/), [Details](https://primer.style/product/components/details/) · Mantine [Collapse](https://mantine.dev/llms/core-collapse.md), [Accordion](https://mantine.dev/llms/core-accordion.md), [Spoiler](https://mantine.dev/llms/core-spoiler.md), [useDisclosure](https://mantine.dev/llms/hooks-use-disclosure.md), [useLocalStorage](https://mantine.dev/llms/hooks-use-local-storage.md), [Button](https://mantine.dev/llms/core-button.md)

## When

- Use when a form or panel has options most users never change (advanced settings, rare filters, raw details).
- Use when long text or a long list would push the main content out of view.
- Not to switch between peer views of the same object → `Tabs`, see [navigation](../decisions/navigation.md).
- Not for rare actions → a "More" `Menu`, see [actions](../decisions/actions.md).
- Not for a short detail about one item on hover or click → `Popover` / `HoverCard`, see [overlays](../decisions/overlays.md).
- Not for a large secondary area (its own settings, history, logs) → give it a separate page and link to it.

Pick the tool:

| Situation | Use | Mantine |
| --- | --- | --- |
| One block of advanced options under a form or panel | Toggle button + collapsible block | `Button variant="subtle"` + `Collapse` |
| Several independent sections, users open one or a few | Accordion | `Accordion` (`multiple` if several may be open) |
| Long text, description or log excerpt | Clamp with "Show more" | `Spoiler` |
| Details about one item | Popover or hover card | see [overlays](../decisions/overlays.md) |
| Peer views | Tabs | see [navigation](../decisions/navigation.md) |
| Rare actions | "More" menu | see [actions](../decisions/actions.md) |

## Anatomy

1. **Trigger**: a button whose label names what it reveals ("Show advanced settings"), with a chevron that
   points down when closed and up when open.
2. **State**: `aria-expanded` on the trigger and `aria-controls` pointing at the revealed block.
3. **Revealed content**: directly under the trigger, in the same column, in the order the user will read it.
4. **Summary** (optional): when closed, a short hint of what is inside or what changed ("2 advanced options set").

## States

| State | What the user sees | Mantine |
| --- | --- | --- |
| Collapsed | Trigger "Show advanced settings", chevron down | `Collapse expanded={false}` |
| Expanded | Trigger "Hide advanced settings", chevron up, content below | `Collapse expanded` |
| Collapsed, values changed | Trigger plus a summary of non-default values | `Text size="sm" c="dimmed"` next to the trigger |
| Collapsed, error inside | Section opens on submit and focus moves to the first error | set expanded state to `true` |
| Long text clamped | First lines and "Show full description" | `Spoiler maxHeight={…}` |

## Rules

**Do**

- Show the common path in full. Hide only what most users do not need for the main task.
- Name what the trigger reveals: "Show advanced settings", "Show full description". Not "More" or "...".
- Pair the label with a chevron icon; flip it with the state. Use a chevron only for expand/collapse, not for
  menus or navigation.
- Put `aria-expanded` and `aria-controls` on custom triggers. `Accordion` and `Spoiler` set them for you.
- Keep revealed content next to the trigger, so the user's point of focus does not move.
- Open a collapsed section automatically when it holds a validation error, and focus the first error.
- Show a summary when hidden options have non-default values, so the user is never surprised by them.
- Keep hidden form values in the form state; collapsing must not reset or lose them.
- Remember the user's choice when they return often (`useLocalStorage`, or the server for cross-device).
- Give `Accordion` an `order` that matches the page's heading levels.

**Don't**

- Don't hide a required field, a field with an error, or information the user needs to decide.
- Don't nest disclosure more than two levels deep. If you need a third, move the content to its own page.
- Don't use text-only "More" / "Less" toggles or a bare icon with no label.
- Don't use `Tabs` or `Accordion` to split a short form that fits on one screen.
- Don't slow the reveal down with long custom `transitionDuration` values. Set `respectReducedMotion: true` in the
  theme so users who ask for less motion get less.
- Don't open disclosure on hover; open on click or keyboard.

## Recipe

```tsx
import { useId } from 'react';
import { Button, Collapse, Group, Spoiler, Stack, Text, TextInput } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { IconChevronDown, IconChevronUp } from '@tabler/icons-react';

function ServerForm() {
  const [advanced, { toggle }] = useDisclosure(false);
  const advancedId = useId();

  return (
    <Stack gap="md">
      <TextInput label="Server name" required />
      <TextInput label="World name" required />

      <Group>
        <Button
          variant="subtle"
          onClick={toggle}
          aria-expanded={advanced}
          aria-controls={advancedId}
          rightSection={advanced ? <IconChevronUp size={16} /> : <IconChevronDown size={16} />}
        >
          {advanced ? 'Hide advanced settings' : 'Show advanced settings'}
        </Button>
      </Group>

      <Collapse expanded={advanced} id={advancedId}>
        <Stack gap="md">
          <TextInput label="Port" description="Leave empty to use the default port." />
          <TextInput label="Save interval (minutes)" />
        </Stack>
      </Collapse>
    </Stack>
  );
}

// Long text
<Spoiler maxHeight={120} showLabel="Show full description" hideLabel="Hide full description">
  <Text>{description}</Text>
</Spoiler>
```

v8: `Collapse` takes `in={advanced}` instead of `expanded={advanced}`.

## Checklist

- [ ] The main task can be completed without opening anything.
- [ ] No required field, error or decision-critical information is hidden.
- [ ] Every trigger names what it reveals and shows its state with a chevron and `aria-expanded`.
- [ ] Revealed content appears directly next to its trigger.
- [ ] A collapsed section with an error opens on submit; hidden values are kept, and summarised when non-default.
- [ ] Disclosure is at most two levels deep.
- [ ] Tabs, menus and popovers are used only for the jobs in the table above.
