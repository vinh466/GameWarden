# Feature onboarding

> **Goal:** users notice a new or unused feature at the moment it helps them, learn it in one step, and can still find it after the hint is gone.
> **Sources:** Primer [Feature onboarding](https://primer.style/product/ui-patterns/feature-onboarding/), [Empty states](https://primer.style/product/ui-patterns/empty-states/), [Banner](https://primer.style/product/components/banner/), [Label](https://primer.style/product/components/label/) · Mantine [Alert](https://mantine.dev/llms/core-alert.md), [Popover](https://mantine.dev/llms/core-popover.md), [Badge](https://mantine.dev/llms/core-badge.md), [NavLink](https://mantine.dev/llms/core-nav-link.md), [Indicator](https://mantine.dev/llms/core-indicator.md), [useLocalStorage](https://mantine.dev/llms/hooks-use-local-storage.md), [Progress](https://mantine.dev/llms/core-progress.md)

## When

- Use when a feature ships that existing users will not find on their own, or a first-time user faces a setup with
  several steps.
- Not for an empty list or page → the [empty state](empty-states.md) is the first teacher; write it to teach.
- Not for system messages (outage, maintenance) → see [degraded experiences](degraded-experiences.md) and
  [feedback](../decisions/feedback.md).
- Not to explain a confusing control. Fix the label or layout first (see [writing](../writing.md)); a hint does not
  repair a bad design.

Pick the tool by the size of the feature and where the user meets it:

| Situation | Use | Mantine |
| --- | --- | --- |
| The area is empty because the feature is unused | Teaching empty state | see [empty states](empty-states.md) |
| A new control on a page the user already uses | One inline hint next to it, dismissible | `Alert variant="light" withCloseButton` |
| The new control sits in a dense toolbar with no room for a hint | A small hint bubble on the control | controlled `Popover withArrow` |
| A new item in navigation or a menu | "New" label for a limited time | `Badge size="xs" variant="light"` |
| Something changed behind an icon (unread, update available) | A dot, sparingly | `Indicator` |
| An early feature that may change | "Beta" label plus a feedback link | `Badge` + `Anchor` |
| A setup with several steps (connect, configure, invite) | First-run checklist that tracks completion | `Checkbox` list or `Stack` + `Progress` |
| Help for one field or setting | Description text or a help link, always visible | input `description`, `Anchor` |

## Anatomy

1. **Placement**: next to the control the hint is about, where the feature will live for good.
2. **Headline**: what the user can now do, in one short line ("Schedule restarts").
3. **Body**: one or two sentences, about 160 characters, on why it helps.
4. **Actions**: a dismiss button ("Got it") and, at most, one "Try it" action or "Learn more" link.
5. **Memory**: a stored "seen" flag (local storage or the server) so the hint never returns after dismissal.
6. **End date**: when the "New" label and the hint are removed from the code.

## States

| State | What the user sees | Mantine |
| --- | --- | --- |
| Not yet seen | Hint next to the new control; the control works normally | `Alert` or `Popover opened` |
| Dismissed or used | No hint; the control stays where it was | stored flag set, hint not rendered |
| New label period | "New" badge on the nav item or menu entry | `Badge` |
| After the period | No badge; the feature is a normal part of the UI | badge removed from code |
| Checklist in progress | Steps with done marks and "2 of 4 done" | `Progress` + text |
| Checklist complete | A short "Setup complete" message, then the checklist goes away | `Alert color="green"` |

## Rules

**Do**

- Introduce a feature at the moment of use: on the page and next to the control where the user needs it.
- Show **one** hint at a time per page. Check what else (banners, other hints) may show on that page.
- Make every hint dismissible with a clear "Got it" button, and keep the page usable while it shows.
- Remember dismissal per user. Use `useLocalStorage` for a device-level flag, the server when it must follow the
  user across devices. Also stop showing the hint once the user has used the feature.
- In a client-only app, pass `getInitialValueInEffect: false` to `useLocalStorage`, so a dismissed hint does not
  flash for one render.
- Keep the feature findable without the hint: a visible control, a clear label, a help link.
- Give "New" and "Beta" labels an end date (for example 30 days) and remove them when it passes.
- Proportion the hint to the feature: a small feature gets a label or one hint, never a page banner.
- If following a "Learn more" link would lose the user's place or unsaved work, open it in a new tab and say so in
  the link text ("Learn more (opens in a new tab)"); otherwise keep the default. See [accessibility](../accessibility.md).
- For multi-step setup, use a checklist that shows progress and marks each step done when the user really did it.
- Render an inline hint with the page. Mantine's `Alert` has `role="alert"`; an alert that pops in later is
  announced and interrupts. Give its close button a `closeButtonLabel`.

**Don't**

- Don't build multi-step product tours for small features. Mantine has no tour component; do not add a tour library
  without a [UX proposal](../process/ux-challenge.md).
- Don't use a modal to announce a feature. It blocks the task the user came to do.
- Don't point a hint at a hidden, collapsed or disabled control.
- Don't show a hint for a feature the user cannot use right now (no permission, not on their plan).
- Don't show a hint again after it was dismissed, or on every page of the same kind.
- Don't move or hide the old way of doing the task while the hint shows.
- Don't put an `Indicator` dot on many items; one dot loses meaning when everything has one.

## Recipe

```tsx
import { Alert, Badge, Button, Group, NavLink, Stack, Text } from '@mantine/core';
import { useLocalStorage } from '@mantine/hooks';
import { IconCalendarTime } from '@tabler/icons-react';

function ScheduleRestartsHint() {
  const [seen, setSeen] = useLocalStorage({
    key: 'hint:schedule-restarts',
    defaultValue: false,
    getInitialValueInEffect: false,
  });
  if (seen) return null;

  return (
    <Alert
      variant="light"
      icon={<IconCalendarTime />}
      title="Schedule restarts"
      withCloseButton
      closeButtonLabel="Dismiss hint"
      onClose={() => setSeen(true)}
    >
      <Stack gap="xs">
        <Text size="sm">Restart the server at a set time every day, so players are not interrupted.</Text>
        <Group>
          <Button size="xs" variant="default" onClick={() => setSeen(true)}>Got it</Button>
        </Group>
      </Stack>
    </Alert>
  );
}

// "New" label on a navigation item for a limited time (remove after the end date).
<NavLink
  href="/schedules"
  label="Schedules"
  rightSection={<Badge size="xs" variant="light">New</Badge>}
/>
```

## Checklist

- [ ] The hint appears next to the feature, on the page where the user needs it.
- [ ] At most one hint shows on a page, and it never blocks the page.
- [ ] Every hint can be dismissed, and a dismissed hint never comes back.
- [ ] The feature is still findable after the hint and the "New" label are gone.
- [ ] Every "New" or "Beta" label has an end date in the plan or code.
- [ ] No modal announcements and no multi-step tours for small features.
- [ ] Multi-step setup uses a checklist that tracks real completion.
- [ ] The hint's close button has an accessible label, and hints are rendered with the page.
