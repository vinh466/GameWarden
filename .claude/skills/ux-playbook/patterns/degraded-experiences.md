# Degraded experiences

> **Goal:** when part of the system fails, the user sees what is affected, keeps using everything that still works, and knows when it is back.
> **Sources:** Primer [Degraded experiences](https://primer.style/product/ui-patterns/degraded-experiences/), [Empty states](https://primer.style/product/ui-patterns/empty-states/), [Banner](https://primer.style/product/components/banner/), [Loading](https://primer.style/product/ui-patterns/loading/) · Mantine [Alert](https://mantine.dev/llms/core-alert.md), [Button](https://mantine.dev/llms/core-button.md), [Tooltip](https://mantine.dev/llms/core-tooltip.md), [useNetwork](https://mantine.dev/llms/hooks-use-network.md), [Notifications](https://mantine.dev/llms/x-notifications.md), [VisuallyHidden](https://mantine.dev/llms/core-visually-hidden.md)

## When

- Use when a request, a dependency or a realtime connection fails and the rest of the screen can still work.
- Use for: backend or realtime connection lost, one service down, partial data, stale data, the browser offline,
  missing permission, a feature switched off.
- Not for a form field the user filled in wrong → see [forms](forms.md).
- Not for "no items" after a successful load → see [empty states](empty-states.md).
- Choosing between alert, banner and toast in general → see [feedback](../decisions/feedback.md).

First decide what is **primary** (the page is useless without it: the record on a detail page, the list on a list
page) and what is **secondary** (counts, side panels, avatars, activity). If primary content fails, show an error
for the page with Retry. If only secondary content fails, render the page and degrade that part.

## Anatomy

1. **Scoped message**: an inline `Alert` in the region that failed, with a warning icon, a title saying what is
   unavailable, and one line on what the user can do.
2. **Retry**: a button in that message, and automatic retry where it is safe.
3. **Banner** (app-wide loss only): one `Alert` at the very top of the app shell, above the navigation.
4. **Freshness**: "Updated 5 min ago" next to data that may be stale.
5. **Unavailable action**: stays visible, looks disabled, and says why next to it.
6. **Recovery**: the region refills by itself; a short status message says it is back.

## States

| State | What the user sees | Mantine |
| --- | --- | --- |
| One region failed | Rest of page normal; that region shows "Players could not be loaded" + Retry | `Alert variant="light" color="yellow"` + `Button` |
| Small item failed (a count, a badge) | The item is hidden, or a short "Unavailable" text | — |
| Partial data | What loaded is shown; a note says which part is missing | `Alert` above the list |
| Stale data | The last data, plus its age and a refresh action | `Text size="xs" c="dimmed"` |
| Realtime connection lost | Data stays; "Live updates paused. Reconnecting…" | inline `Alert` or app banner |
| Offline | App banner "You are offline"; reading works, saving waits | `useNetwork().online` + `Alert` |
| Missing permission | "You need the Admin role to change this. Ask an owner." | `Alert color="gray"` or text |
| Feature switched off | Says it is turned off and where to turn it on, or who can | `Alert` or text |
| Primary content failed | Error for the page with Retry; navigation still visible | `Alert` + `Button` |
| Recovered | Content returns in place; "Connection restored" announced | `role="status"` text or a short notification |

## Rules

**Do**

- Keep everything that still works usable. Render the page and degrade only the part that failed.
- Scope the message to the failed part: put it in that region, not in a toast that disappears.
- Use one app-wide banner only for app-wide loss (offline, backend down). Keep the in-context messages too.
- Keep the last good data on screen when a refresh fails; show its age ("Updated 5 min ago").
- Say what is unavailable in the user's words and what they can do: retry, wait, or who to ask.
- Retry automatically with backoff (for example 1 s, 2 s, 4 s, then stop) for safe reads and reconnects. Always
  offer a manual Retry too.
- Keep critical actions visible when they cannot work. Make them look disabled with `data-disabled`, keep them
  focusable, show the reason in text next to them, and link it with `aria-describedby`.
- Remove non-critical actions and indicators (a reaction button, an unread dot, a count) that depend on failed data.
- Keep typed input when an action fails; let the user retry when the connection is back.
- Recover without a click when possible. Announce recovery once ("Connection restored") in a `role="status"` region.
- Keep the app navigation rendered; drop only the links that cannot be built.
- Keep the number of error messages on one page small (Primer suggests five at most).

**Don't**

- Don't show an empty state for data that failed to load. "No servers" after a failure reads as data loss.
- Don't blank the page, or replace it with an error, when only a secondary part failed.
- Don't show raw error objects, stack traces, request IDs without context, or internal service names.
- Don't use the `disabled` attribute on a critical action that fails for availability reasons: it cannot be focused
  and a tooltip on it will not open. Use `data-disabled` and a visible reason.
- Don't rely on a tooltip alone for the reason; tooltips do not open on touch by default.
- Don't auto-retry writes that are not safe to repeat (payments, sends). Ask the user.
- Don't keep a "Reconnecting…" spinner forever; after the retries stop, say so and offer Retry.
- Don't open a dialog whose content failed; hide its trigger if it is non-critical, or explain inside it if it is.

## Recipe

```tsx
import { useId } from 'react';
import { Alert, Button, Group, Stack, Text } from '@mantine/core';
import { useNetwork } from '@mantine/hooks';
import { IconAlertTriangle, IconRefresh, IconWifiOff } from '@tabler/icons-react';

// Region failed: message in place of the region, with Retry.
function RegionError({ onRetry, retrying }: { onRetry: () => void; retrying: boolean }) {
  return (
    <Alert variant="light" color="yellow" icon={<IconAlertTriangle />} title="Players could not be loaded">
      <Stack gap="xs" align="flex-start">
        <Text size="sm">The rest of the page still works. Try again in a moment.</Text>
        <Button size="xs" variant="default" leftSection={<IconRefresh size={14} />} loading={retrying} onClick={onRetry}>
          Retry
        </Button>
      </Stack>
    </Alert>
  );
}

// App-wide: one banner at the top of the app shell while the browser is offline.
function OfflineBanner() {
  const { online } = useNetwork();
  if (online) return null;
  return (
    <Alert variant="light" color="yellow" icon={<IconWifiOff />} title="You are offline">
      You can keep reading. Changes cannot be saved until the connection returns.
    </Alert>
  );
}

// Critical action that cannot work right now: visible, focusable, reason in text.
function SaveAction({ online, onSave }: { online: boolean; onSave: () => void }) {
  const reasonId = useId();
  return (
    <Group gap="sm">
      <Button
        data-disabled={!online || undefined}
        aria-describedby={online ? undefined : reasonId}
        onClick={online ? onSave : (event) => event.preventDefault()}
      >
        Save changes
      </Button>
      {!online && <Text id={reasonId} size="sm" c="dimmed">Saving is paused while you are offline.</Text>}
    </Group>
  );
}

// Stale data: keep it and show its age.
<Text size="xs" c="dimmed">Updated 5 min ago</Text>
```

`useNetwork().online` reports the browser's connection; it does not prove your backend is reachable. Treat failed
requests as the real signal for "backend down". The other fields (`rtt`, `effectiveType`) use the
experimental `navigator.connection` API and may be `undefined`.

## Checklist

- [ ] Every data region has an error state, scoped to that region, with Retry.
- [ ] A secondary failure does not blank the page; primary content failing shows a page error with navigation intact.
- [ ] A failed load never shows an empty state.
- [ ] Stale data stays visible with its age.
- [ ] App-wide loss (offline, backend down) shows one banner, and the page still works where it can.
- [ ] Unavailable critical actions stay visible and focusable, with a visible reason; non-critical ones are removed.
- [ ] No raw errors, stack traces or internal names appear in the UI.
- [ ] Safe reads retry with backoff and stop; recovery happens without a click and is announced once.
