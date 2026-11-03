# Feedback: choosing the component

> **Use when:** the UI must tell the user the result of an action, an error, a warning or a system condition, and you are choosing between a toast, an `Alert`, a page banner, a field error or status text.
> **Sources:** Primer [Notification messaging](https://primer.style/product/ui-patterns/notification-messaging/), [Accessible notifications and messages](https://primer.style/accessibility/patterns/accessible-notifications-and-messages/), [Banner](https://primer.style/product/components/banner/guidelines/), [InlineMessage](https://primer.style/product/components/inline-message/), [Saving](https://primer.style/product/ui-patterns/saving/) · Mantine [Notifications system](https://mantine.dev/llms/x-notifications.md), [Notification](https://mantine.dev/llms/core-notification.md), [Alert](https://mantine.dev/llms/core-alert.md), [Input](https://mantine.dev/llms/core-input.md), [VisuallyHidden](https://mantine.dev/llms/core-visually-hidden.md)

## Decide

Pick by **scope** (where the cause is), **severity**, and **persistence** (must it stay until read or fixed?).

| Situation | Use | Not | Why |
| --- | --- | --- | --- |
| One field's value is invalid | The input's `error` prop, under the field | Toast; only a form-level `Alert` | The message sits at the cause; `error` sets `aria-invalid` |
| Submit failed: several fields, or a server error not tied to a field | `Alert color="red"` at the top of the form, plus field errors | Toast | It must stay while the user fixes it |
| The result is already visible (item appears, edit mode closes, page shows the new item) | Nothing visible; announce it in a `role="status"` text | Success toast | Extra success messages add noise and lower trust |
| Low-stakes success the user cannot see (link copied, invite sent, saved in another panel) | Short status text next to the trigger; or a toast with `autoClose` | `Alert` banner | Nothing to do next, so it may disappear |
| An action failed and the user must act (retry, fix, contact someone) | `Alert color="red"` near the trigger, or at the top of the region, with the action in it | Auto-closing toast | Errors that need action must persist |
| An action inside a `Modal` failed | `Alert` at the top of the modal body; keep the modal open | Close the modal and show a page banner | The user keeps their input and context |
| Background work the user may leave (export, import, bulk job) | One notification: `loading` → `notifications.update` to success or error | A blocking modal; a second notification for the result | One message that changes state in place |
| A condition for the whole page or section (read-only, missing permission, limit reached, degraded) | One `Alert` at the top of the page content or section | Toast; a modal | It applies as long as the condition lasts |
| A system announcement for the whole app (maintenance, outage) | One `Alert` at the top of the `AppShell.Main` content, under the header | Modal; toast | Visible on every page without blocking work |
| A consequence the user should know **before** acting (cost, access change) | `Alert color="yellow"` next to the action or inside its dialog | A warning after the fact | Warn where the decision is made |
| Quiet status of a control or view ("Saving…", "Saved", "12 results") | `Text size="sm" c="dimmed"` that is also a `role="status"` region | Toast | See [saving](../patterns/saving.md) |
| Confirm before deleting or overwriting | See [destructive actions](../flows/destructive-actions.md) | Toast | — |

Severity uses the same colour, icon and word everywhere. Never colour alone.

| Severity | Colour | Icon (`@tabler/icons-react`) | Use for |
| --- | --- | --- | --- |
| Info | `blue` | `IconInfoCircle` | Context that helps the task |
| Success | `green` | `IconCircleCheck` | A completed action the user cannot otherwise see |
| Warning | `yellow` | `IconAlertTriangle` | A possible problem or a side effect |
| Error | `red` | `IconAlertCircle` | A failed action, invalid input, data at risk |
| Unavailable | `gray` | `IconCloudOff` | A part that could not load, a missing permission |

## Rules

1. Put the message next to its cause: field → under the input; action → next to its button; region → top of the
   region; dialog → top of the dialog body; page → top of the page content, inside the content width.
2. Use a toast (`notifications.show`) only to confirm a low-stakes action the user just took, when the result is not
   visible. A toast is never the only place for an error that needs action, for information the user must keep, or
   for the only way to recover (Retry, Undo).
3. Keep error messages until the cause is fixed or the user dismisses them: `autoClose: false` on error
   notifications; no close button on an error `Alert` that blocks the task.
4. Show at most one page-level banner at a time. Merge related messages into one `Alert`; never stack banners.
5. Keep toasts short (a title and one sentence) and leave the default 4 s auto-close or longer. Do not set a shorter
   `autoClose`. Mantine pauses the timer while the pointer is over a notification.
6. For background work, show one notification with `loading: true, autoClose: false` and keep its `id`. Change it
   with `notifications.update({ id, ... })` when the work ends, or use `notifications.promise`. Never add a second
   notification for the result.
7. Do not flood: set `limit` on `<Notifications />` (Mantine's default is 5; 3 is plenty) and summarise bulk actions
   in one message ("12 servers restarted"), never one toast per item.
8. Give every message a title or first words that state the outcome ("Couldn't save changes"), then what to do.
   Wording: [writing](../writing.md).
9. Announce without stealing focus. Mantine's `Notification` and `Alert` render `role="alert"` (announced at once):
   right for errors. For quiet success or status, use an always-mounted `role="status"` text and change only its
   content. `Alert` passes a `role` prop through, so `role="status"` works for a non-urgent `Alert` added later.
10. Field validation does not rely on live regions: set `error` on the input and move focus to the first invalid
    field on submit. See [forms](../patterns/forms.md).
11. A banner the user cannot fix (info, an organisation-wide notice) may be dismissible: `withCloseButton` with
    `closeButtonLabel`. Remember the dismissal. A banner about a condition the user must resolve is not dismissible.
12. A banner has at most two actions: one primary (the recommended step), one secondary.
13. Keep one notification position app-wide (set `position` on `<Notifications />` once, not per call).

## Recipes

### Setup, once inside `MantineProvider`

```tsx
import '@mantine/notifications/styles.css';
import { Notifications } from '@mantine/notifications';

<Notifications position="bottom-right" limit={3} />
```

### Low-stakes confirmation

```tsx
import { notifications } from '@mantine/notifications';
import { IconCircleCheck } from '@tabler/icons-react';

notifications.show({
  color: 'green',
  icon: <IconCircleCheck size={18} />,
  title: 'Invite sent',
  message: 'Ana will get an email with a link to join.',
});
```

### Background work, updated in place

```tsx
import { notifications } from '@mantine/notifications';
import { IconAlertCircle } from '@tabler/icons-react';

const id = notifications.show({
  loading: true,
  autoClose: false,
  allowClose: false,
  title: 'Exporting world',
  message: 'You can keep working. This takes about a minute.',
});
// when it ends with an error: the error stays until dismissed
notifications.update({
  id,
  loading: false,
  allowClose: true,
  autoClose: false,
  color: 'red',
  icon: <IconAlertCircle size={18} />,
  title: "Couldn't export world",
  message: 'The server stopped responding. Try again from the Backups tab.',
});
```

v8: there is no `allowClose`, `notifications.promise` or `priority`; use `withCloseButton: false` while loading.

### Error banner for a region or form

```tsx
import { Alert, Button, Stack, Text } from '@mantine/core';
import { IconAlertCircle } from '@tabler/icons-react';

<Alert color="red" icon={<IconAlertCircle />} title="Couldn't load backups">
  <Stack gap="xs" align="flex-start">
    <Text size="sm">The storage service did not answer. Your backups are safe.</Text>
    <Button size="xs" variant="default" onClick={retry}>Try again</Button>
  </Stack>
</Alert>
```

### Status text next to the trigger

```tsx
import { Button, Group, Text } from '@mantine/core';
import { IconCopy } from '@tabler/icons-react';

<Group gap="xs">
  <Button variant="default" leftSection={<IconCopy size={16} />} onClick={copy}>Copy link</Button>
  <Text size="sm" c="dimmed" role="status">{copied ? 'Copied' : ''}</Text>
</Group>
```

## Pitfalls

- A toast for "Couldn't save" that closes after 4 s. Fix: an `Alert` at the form or region, or `autoClose: false`.
- `notifications.promise` resets `autoClose` to the default for the error state, so errors vanish. Fix: pass
  `autoClose: false` in the `error` object.
- A success toast after every save, while the page already shows the change. Fix: drop it; use status text.
- A modal closes on failure and the error appears on the page behind it. Fix: keep the modal open with an `Alert`.
- Several page banners stacked at the top. Fix: one banner per scope; merge or prioritise.
- A red border with no words, or "Error" with no next step. Fix: icon + colour + a sentence that says what to do.
- A status element that mounts together with its text is often not announced. Fix: keep the `role="status"`
  element mounted and change its text.
- `title` and `message` that repeat each other. Fix: the title states the outcome, the message gives the reason or step.

## Checklist

- [ ] Each message sits next to its cause (field, button, region, dialog, page).
- [ ] No error that needs action appears only in a toast; error notifications use `autoClose: false`.
- [ ] Success messages appear only when the result is not already visible.
- [ ] Background work uses one notification, updated in place from loading to result.
- [ ] At most one page-level banner is visible; `<Notifications />` has a `limit`.
- [ ] Every message has colour + icon + words, using the severity table.
- [ ] Quiet status changes are announced through an always-mounted `role="status"` element.
- [ ] Dismissible banners have `closeButtonLabel`; banners about unresolved conditions are not dismissible.
- [ ] Failed actions in a modal show the error inside the modal, which stays open with the user's input.
