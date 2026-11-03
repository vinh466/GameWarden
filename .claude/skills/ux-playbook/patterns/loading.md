# Loading

> **Goal:** while the user waits, they know something is happening, where, and roughly how long, and the rest of the app stays usable.
> **Sources:** Primer [Loading](https://primer.style/product/ui-patterns/loading/), [SkeletonBox](https://primer.style/product/components/skeleton-box/), [Spinner](https://primer.style/product/components/spinner/), [ProgressBar](https://primer.style/product/components/progress-bar/), [Motion and animation](https://primer.style/accessibility/design-guidance/motion-and-animation/) · Mantine [Skeleton](https://mantine.dev/llms/core-skeleton.md), [Loader](https://mantine.dev/llms/core-loader.md), [LoadingOverlay](https://mantine.dev/llms/core-loading-overlay.md), [Fieldset](https://mantine.dev/llms/core-fieldset.md), [Progress](https://mantine.dev/llms/core-progress.md), [NavigationProgress](https://mantine.dev/llms/x-nprogress.md), [Button](https://mantine.dev/llms/core-button.md), [Notifications](https://mantine.dev/llms/x-notifications.md), [VisuallyHidden](https://mantine.dev/llms/core-visually-hidden.md), [Theme object](https://mantine.dev/llms/theming-theme-object.md), [useReducedMotion](https://mantine.dev/llms/hooks-use-reduced-motion.md)

## When

- Use when a page, region or control waits for data, or an action waits for the server.
- Not for a failed load → see [degraded experiences](degraded-experiences.md).
- Not for the save flow of a form (dirty, saving, saved) → see [saving](saving.md).
- Not for a toast after the work is done → see [feedback](../decisions/feedback.md).

## Anatomy

1. **Indicator**: a skeleton, a `Loader`, a button's `loading` state or a `Progress` bar, placed where the result will appear.
2. **Label** (optional, visible or hidden): what is loading ("Loading servers"), for sighted and screen-reader users.
3. **Status message**: one `role="status"` region that announces the result (loaded, count, failed).
4. **Way out** (long work only): Cancel, or "Continue in background".

Choose the indicator by the wait and the scope:

| Wait or scope | Use | Mantine |
| --- | --- | --- |
| Usually under ~1 s | Nothing new; keep the old content | — |
| A page or region loads for the first time | Skeleton in the shape of the content | `Skeleton` |
| Small inline wait (a select's options, a cell) | Small spinner next to or in place of the value | `Loader size="xs"` |
| A button's action runs | The button shows it is busy | `Button loading` |
| Refresh where the old content must stay visible but must not be edited | Overlay on that region only | `LoadingOverlay` in a `Box pos="relative"` |
| Progress is known and the wait is ~3 s or more | Determinate bar with text ("4 of 12") | `Progress value={…}` |
| Route change | Thin bar at the top of the viewport | `NavigationProgress` from `@mantine/nprogress` |
| Work over ~10 s (import, export, build) | Progress + Cancel, or move to background and notify on completion | `Progress`, `notifications.show` / `notifications.update` |

## States

| State | What the user sees | Mantine |
| --- | --- | --- |
| Started | The control responds at once (button busy, bar starts) | `Button loading`, `nprogress.start()` |
| In progress | Indicator where the content will be; rest of the page usable | `Skeleton`, `Loader`, `Progress` |
| Partly loaded | Items that arrived are shown; the rest still loading | content + `Skeleton` for the rest |
| Done | Content replaces the indicator in the same spot, no jump | — |
| Done in background | A notification that names the result and links to it | `notifications.update({ id, loading: false, … })` |
| Failed | Error in the same spot with Retry | see [degraded experiences](degraded-experiences.md) |

## Rules

**Do**

- Place the indicator where the content will appear, sized like that content, so nothing jumps when it arrives.
- Give skeletons the shape of the real content: rows for a table, lines for text, a circle for an avatar.
- Show items as they arrive; load the most important part first. Do not wait for everything.
- Use one indicator for a group of things loading together, not one spinner per cell.
- Keep the rest of the app usable. Load per region, not per page.
- Keep the button that started the work in place and busy (`loading`). Its width must not change.
- When you use `LoadingOverlay`, also stop input under it: the overlay does not block the keyboard. Wrap the fields
  in `Fieldset disabled` or disable them.
- Show a determinate `Progress` with a text value when you know the progress; label it with `aria-label`.
- For long work, let the user leave or cancel; continue in the background and send a notification when it ends.
- Set `aria-busy="true"` on the region while it loads, and announce the result once in an always-rendered
  `role="status"` element ("12 servers loaded", "No results").
- Move focus only if the user's next step is in the new content; never steal focus on a background load.
- Set `respectReducedMotion: true` in the theme so Mantine transitions shorten for users who ask for less motion.
  Keep spinners turning (they prove the app is not frozen), but small and subtle. With `useReducedMotion()` you
  may stop the skeleton pulse (`animate={false}`).

**Don't**

- Don't flash an indicator for a wait under ~1 s. If your data library can delay the loading state, use it.
- Don't block the whole app or page with an overlay or full-screen spinner for a regional load.
- Don't nest spinners (a spinner in a card in a spinning page) or show many spinners at once.
- Don't show "0 items" or an empty state before the first load has finished.
- Don't fake a determinate bar that jumps from 10 % to 100 %; use an indeterminate indicator instead.
- Don't use an interstitial loading page unless the work takes several seconds and ends on a new page.
- Don't let a busy button be clicked twice; Mantine's `loading` disables it while the work runs.

## Recipe

```tsx
import { Box, Button, Progress, Skeleton, Stack, Text, VisuallyHidden } from '@mantine/core';
import { useReducedMotion } from '@mantine/hooks';

// Region load: skeleton in the shape of three list rows, one announcement for the whole region.
function ServerListSkeleton() {
  const reduceMotion = useReducedMotion();
  return (
    <Stack gap="sm">
      {[0, 1, 2].map((row) => (
        <Skeleton key={row} height={48} radius="md" animate={!reduceMotion} />
      ))}
    </Stack>
  );
}

function ServerList({ loading, servers }: { loading: boolean; servers: string[] }) {
  return (
    <Box aria-busy={loading}>
      {loading ? <ServerListSkeleton /> : servers.map((name) => <Text key={name}>{name}</Text>)}
      <VisuallyHidden role="status">
        {loading ? 'Loading servers' : `${servers.length} servers loaded`}
      </VisuallyHidden>
    </Box>
  );
}

// Action: the button shows the pending state itself.
<Button loading={saving} onClick={save}>Save changes</Button>

// Known progress: bar plus visible text.
<Stack gap="xs">
  <Progress value={33} aria-label="Import progress" />
  <Text size="sm" c="dimmed">4 of 12 files imported</Text>
</Stack>
```

Route changes: render `<NavigationProgress />` once inside `MantineProvider`, import `@mantine/nprogress/styles.css`
after the core styles, and call `nprogress.start()` / `nprogress.complete()` from the router's navigation events.

Background work (`@mantine/notifications`): show a loading notification, then update it when the work ends.

```tsx
const id = notifications.show({ loading: true, autoClose: false, title: 'Exporting world', message: 'You can keep working.' });
// later, when the job finishes:
notifications.update({ id, loading: false, autoClose: 4000, title: 'Export ready', message: 'Download it from Exports.' });
```

## Checklist

- [ ] Every data view has a loading state, and it appears where the content will appear.
- [ ] Skeletons match the shape of the content; the layout does not jump when data arrives.
- [ ] No indicator flashes for fast loads; no nested or repeated spinners.
- [ ] Buttons that start work use `loading` and cannot be clicked twice.
- [ ] A `LoadingOverlay` covers only its region, and the inputs under it are disabled.
- [ ] Known progress uses `Progress` with `aria-label` and a text value.
- [ ] Work over ~10 s can be cancelled or continues in the background with a completion notification.
- [ ] The region has `aria-busy` while loading, and the result is announced once in a `role="status"` element.
- [ ] The theme sets `respectReducedMotion: true`; loaders stay small and subtle.
