# Empty states

> **Goal:** when a region has nothing to show, the user knows why and what to do next.
> **Sources:** Primer [Empty states](https://primer.style/product/ui-patterns/empty-states/), [Blankslate](https://primer.style/product/components/blankslate/), [Degraded experiences](https://primer.style/product/ui-patterns/degraded-experiences/) · Mantine [Stack](https://mantine.dev/llms/core-stack.md), [ThemeIcon](https://mantine.dev/llms/core-theme-icon.md), [Title](https://mantine.dev/llms/core-title.md), [Button](https://mantine.dev/llms/core-button.md), [Anchor](https://mantine.dev/llms/core-anchor.md)

## When

- Use when a list, table, panel or page loaded successfully and has no items.
- Use for three kinds of emptiness, each with its own wording:
  - **First use**: the user has not created anything yet. Teach what the area is for.
  - **No results**: a search or filter matched nothing. The data exists; the query hides it.
  - **Cleared**: the user finished the work ("All caught up"). Nothing is wrong.
- Not while data is still loading → see [loading](loading.md).
- Not when loading failed or a service is down → see [degraded experiences](degraded-experiences.md). An error
  must never look like "you have nothing".
- Not for introducing a new feature on a populated page → see [feature onboarding](feature-onboarding.md).

## Anatomy

1. **Icon** (optional): a `ThemeIcon` with one Tabler icon that stands for the feature. No illustration on errors.
2. **Title**: one short line that names the situation ("No servers yet", "No results for "alpha"").
3. **Description**: one sentence on what the area is for or what to try next.
4. **Primary action** (at most one): starts the creation flow, or clears the filters.
5. **Secondary link** (optional): "Learn more about …", placed under the action. First use only.

Primer calls this component a *Blankslate*. Mantine has none; build one `EmptyState` (Recipe) and reuse it.

## States

| State | What the user sees | Mantine |
| --- | --- | --- |
| First use | Feature icon, "No servers yet", what servers do, "Create server" | `EmptyState` + `Button` |
| First use, no permission | Same title, description says who can create one; no action | `EmptyState` without `action` |
| No results | "No results for "alpha"", hint to change the query, "Clear filters" | `EmptyState` + `Button variant="default"` |
| Cleared | Check icon, "All caught up", no action needed | `EmptyState` without `action` |
| Load failed | Not an empty state: error message in the region with Retry | see [degraded experiences](degraded-experiences.md) |

## Rules

**Do**

- Render the empty state **inside the region it replaces**: the table body, the card, the panel. Keep the page
  header, filters and the toolbar visible around it.
- Use a full-page empty state only when the whole page is that one feature and it is empty.
- Keep the filters and the search box visible on "no results", so the user can see and change what hides the data.
- Echo the query in the title when it is short ("No results for "alpha"").
- Make the primary action the same action the page offers when it has data (same label, same flow).
- Start the description with the benefit or the next step, not with "There are no …" again.
- Show the action only if the user is allowed to do it. Otherwise say who can.
- Announce the result count after a search or filter in a `role="status"` region (see [loading](loading.md)).
- Keep it calm: a muted icon, `Text c="dimmed"` for the description, centred in the region.
- Build **one shared `EmptyState` component** in the product and use it everywhere. Do not hand-roll one per page.

**Don't**

- Don't show an empty state while data is loading; show the loading state, then the result.
- Don't show "No items" when loading failed. The user will think the data was deleted.
- Don't use playful graphics or jokes on errors, no-permission or "no results" states.
- Don't put more than one primary button in an empty state.
- Don't hide the "Create" action elsewhere on the page just because the empty state also has one: once items exist,
  the empty state disappears and the toolbar action must still be there.
- Don't show raw codes ("404", "null") or internal names.

Wording for titles and descriptions: [writing](../writing.md).

## Recipe

```tsx
import type { ReactNode } from 'react';
import { Anchor, Button, Stack, Text, ThemeIcon, Title } from '@mantine/core';
import { IconFilterOff, IconServer } from '@tabler/icons-react';

type EmptyStateProps = {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
  learnMore?: { href: string; label: string };
};

// One shared component for every empty state in the product.
export function EmptyState({ icon, title, description, action, learnMore }: EmptyStateProps) {
  return (
    <Stack align="center" gap="xs" py="xl" ta="center">
      {icon && (
        <ThemeIcon variant="light" color="gray" size="xl" radius="xl" aria-hidden>
          {icon}
        </ThemeIcon>
      )}
      <Title order={3} size="h4">{title}</Title>
      {description && <Text c="dimmed" size="sm">{description}</Text>}
      {action && <Stack mt="sm">{action}</Stack>}
      {learnMore && <Anchor href={learnMore.href} size="sm">{learnMore.label}</Anchor>}
    </Stack>
  );
}

// First use
<EmptyState
  icon={<IconServer />}
  title="No servers yet"
  description="Servers run your game worlds. Create one to invite players."
  action={<Button onClick={openCreate}>Create server</Button>}
  learnMore={{ href: '/docs/servers', label: 'Learn more about servers' }}
/>

// No results from a search or filter
<EmptyState
  icon={<IconFilterOff />}
  title={`No results for "${query}"`}
  description="Try a different name or clear the filters."
  action={<Button variant="default" onClick={clearFilters}>Clear filters</Button>}
/>
```

## Checklist

- [ ] Every list, table and panel has an empty state, and it appears only after loading succeeded.
- [ ] First use, no results and cleared states have different wording.
- [ ] The empty state sits inside the region it replaces; header, filters and toolbar stay visible.
- [ ] There is at most one primary action, and it matches the page's own create action.
- [ ] "No results" offers a way to clear filters or change the query.
- [ ] A failed load shows an error with Retry, never an empty state.
- [ ] The product uses one shared `EmptyState` component.
- [ ] The icon is decorative (`aria-hidden`); the title is a real heading at the right level.
