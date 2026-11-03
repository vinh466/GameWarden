# Saving

> **Goal:** the user always knows whether their change is saved, never loses what they entered, and is warned before leaving unsaved work.
> **Sources:** Primer [Saving](https://primer.style/product/ui-patterns/saving/), [Forms](https://primer.style/product/ui-patterns/forms/), [ToggleSwitch](https://primer.style/product/components/toggle-switch/guidelines/), [SegmentedControl](https://primer.style/product/components/segmented-control/guidelines/), [Notification messaging](https://primer.style/product/ui-patterns/notification-messaging/) · Mantine [use-form](https://mantine.dev/llms/form-use-form.md), [Form status](https://mantine.dev/llms/form-status.md), [Form values](https://mantine.dev/llms/form-values.md), [Form errors](https://mantine.dev/llms/form-errors.md), [useWindowEvent](https://mantine.dev/llms/hooks-use-window-event.md), [useDebouncedCallback](https://mantine.dev/llms/hooks-use-debounced-callback.md), [Button](https://mantine.dev/llms/core-button.md), [Switch](https://mantine.dev/llms/core-switch.md), [Alert](https://mantine.dev/llms/core-alert.md), [Notifications system](https://mantine.dev/llms/x-notifications.md), [Style props](https://mantine.dev/llms/styles-style-props.md)

## When

- Use when the user changes stored data or settings: an edit form, a settings page, a toggle, a note.
- Not for field labels and validation → see [forms](forms.md).
- Not for delete, reset or overwrite → see [destructive actions](../flows/destructive-actions.md).
- Not for long background jobs (export, import) → see [loading](loading.md) and [feedback](../decisions/feedback.md).

Choose one save model per form:

| Model | Use for | Not for | Mantine |
| --- | --- | --- | --- |
| **Explicit Save** (default) | Any form of several fields; text inputs; checkbox and radio groups; selects and multi-selects in a form; anything sensitive | — | `<form>` + `Button type="submit"`, `@mantine/form` |
| **Immediate** | One `Switch`; a `SegmentedControl`; a single non-native select or menu outside any form | Text, groups, `NativeSelect`, passwords | the control's `onChange` calls the API |
| **Autosave** | Long free text the user writes over time (notes, descriptions, drafts), when the product already autosaves | Settings, secrets, fields with validation | `useDebouncedCallback` + status text |

## Anatomy

1. **Save button**: one per form, named with a verb ("Save changes", "Save backup settings"). Primary when it saves
   the whole page; secondary when it saves one section of a larger page.
2. **Discard / Cancel**: secondary, next to Save; returns to the last saved values (`form.reset()`).
3. **Status text**: next to the Save button, in a `role="status"` element: "Unsaved changes", "Saving…",
   "Saved · 2 minutes ago", "Couldn't save".
4. **Sticky action bar** (long forms): Save, Discard and status pinned to the bottom of the viewport.
5. **Leave guard**: the browser's `beforeunload` prompt, plus the router's blocker for in-app navigation.
6. **Error or conflict `Alert`**: at the top of the form, with the next step.

## States

| State | What the user sees | Mantine |
| --- | --- | --- |
| Clean | Save enabled; status shows the last save time or nothing | `form.isDirty()` is `false` |
| Dirty | "Unsaved changes"; leaving the page asks first | `form.isDirty()` is `true`; `useWindowEvent('beforeunload', …)` |
| Saving | Save shows a spinner and cannot be clicked twice; values stay | `Button loading={form.submitting}` |
| Saved | "Saved · just now" next to Save, or the user lands on the saved item | `form.setInitialValues(values)` + `form.resetDirty(values)` |
| Failed | Values kept; `Alert` or status says what failed and to try again; Save still enabled | `form.setErrors(...)` for field errors |
| Conflict | `Alert`: someone else changed it; the user's input is kept; "Reload latest" and "Keep editing" | `Alert` with buttons |
| Immediate, optimistic | The control changes at once; on failure it flips back and an error says so | `useState` + revert in `catch` |
| Autosaving | "Saving…" then "Saved" in quiet status text; "Not saved, retrying" on failure | `useDebouncedCallback` |

## Rules

**Do**

- Start with explicit Save. Use one save model per form, and keep immediate controls visually apart from a form
  with a Save button (a separate section or card).
- Keep one Save button per form. Pages with several independently saved sections name each button
  ("Save notification settings") so screen-reader and voice users can tell them apart.
- Keep Save enabled when the form is clean or invalid. A click on an invalid form shows the errors
  ([forms](forms.md)); a click on a clean form may simply confirm "Saved".
- Show the result where the user is looking: status text next to Save when the page stays; the saved item or a
  page-level `Alert` after a redirect. If the change is visible on screen, the status text is enough.
- Show "Saved" with a relative time ("Saved · 2 minutes ago") that updates, with the exact time in a `Tooltip`.
- After a successful save, make the saved values the new baseline: `form.setInitialValues(values)` and
  `form.resetDirty(values)`.
- Warn before leaving unsaved changes: `beforeunload` when `form.isDirty()`; browsers show their own generic text.
  In-app navigation needs the router's blocker (the app's job) and a confirm `Modal` ("Leave without saving" / "Stay").
- Pin Save to the bottom of the viewport for forms taller than one screen, so the user never scrolls to find it.
- On an immediate control, update the UI at once (optimistic), call the API, and on failure revert the control and
  show an error that stays ([feedback](../decisions/feedback.md)).
- Autosave after a pause in typing (about 1 s) and on blur; flush pending changes on unmount
  (`flushOnUnmount: true`). Show "Saving…" / "Saved" and keep the text locally until the save succeeds.
- Detect conflicts: send the version you loaded (an ETag or `updatedAt`) with the save. When the server rejects it,
  keep the user's input, say who or what changed it if known, and offer "Reload latest" (warn that it discards their
  changes) and "Keep editing". Overwriting someone else's change is a destructive action: confirm it.
- Keep every value on any failure: network error, timeout, conflict, expired session (sign in again, then retry).
- For risky changes, offer undo after saving, or confirm before ([destructive actions](../flows/destructive-actions.md)).

**Don't**

- Don't autosave text inputs in settings, checkbox or radio groups, `NativeSelect`, multi-selects or passwords.
  Keyboard users change a radio or select just by moving through the options.
- Don't put a Save button next to controls that already save on change, and don't mix models in one form.
- Don't show "Saved" next to a `Switch`; its new position is the confirmation, and a tick can read as validation.
- Don't disable Save to say "nothing changed" or "invalid"; a disabled button cannot be focused and does not say why.
- Don't save a multi-select when its dropdown closes; save it with the form.
- Don't show a success toast and a status text for the same save. Pick one.
- Don't clear the form, close the dialog or navigate away after a failed save.

## Recipe

```tsx
import { useState } from 'react';
import { Button, Group, Stack, Text, Textarea, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useWindowEvent } from '@mantine/hooks';

export function ServerSettingsForm({ initial }: { initial: { name: string; motd: string } }) {
  const [status, setStatus] = useState('');
  const form = useForm({ mode: 'uncontrolled', initialValues: initial, onValuesChange: () => setStatus('Unsaved changes') });

  useWindowEvent('beforeunload', (event) => {
    if (form.isDirty()) event.preventDefault(); // the browser shows its own "Leave site?" prompt
  });

  const save = async (values: typeof form.values) => {
    setStatus('Saving…');
    try {
      await saveSettings(values); // placeholder API call
      form.setInitialValues(values);
      form.resetDirty(values);
      setStatus('Saved · just now'); // replace with the product's relative-time helper
    } catch {
      setStatus("Couldn't save. Your changes are still here. Try again.");
    }
  };

  return (
    <form noValidate onSubmit={form.onSubmit(save)}>
      <Stack gap="md">
        <TextInput label="Server name" key={form.key('name')} {...form.getInputProps('name')} />
        <Textarea label="Message of the day" autosize minRows={3} key={form.key('motd')} {...form.getInputProps('motd')} />
        <Group pos="sticky" bottom={0} py="sm" gap="sm" bg="var(--mantine-color-body)">
          <Button type="submit" loading={form.submitting}>Save changes</Button>
          <Button variant="default" onClick={() => { form.reset(); setStatus(''); }}>Discard</Button>
          <Text size="sm" c="dimmed" role="status">{status}</Text>
        </Group>
      </Stack>
    </form>
  );
}
```

Immediate control with rollback:

```tsx
import { Switch } from '@mantine/core';
import { notifications } from '@mantine/notifications';

const [backups, setBackups] = useState(initialBackups);
const toggle = async (next: boolean) => {
  setBackups(next);
  try {
    await saveBackups(next); // placeholder API call
  } catch {
    setBackups(!next);
    notifications.show({ color: 'red', autoClose: false, title: "Couldn't change automatic backups", message: 'Check your connection and try again.' });
  }
};
<Switch label="Automatic backups" checked={backups} onChange={(e) => toggle(e.currentTarget.checked)} />
```

## Checklist

- [ ] Each form uses one save model; immediate controls sit outside forms that have a Save button.
- [ ] One Save button per form, named with a verb, enabled even when clean or invalid.
- [ ] Saving shows `loading` on the button; the result appears next to it or on the page the user lands on.
- [ ] After saving, the dirty baseline is reset; "Unsaved changes" disappears.
- [ ] Leaving a dirty form triggers `beforeunload`, and in-app navigation asks first.
- [ ] Long forms keep Save visible in a sticky bar.
- [ ] Immediate controls revert and show a lasting error when the save fails.
- [ ] Autosave shows "Saving…" / "Saved", flushes on blur and unmount, and never drops text on failure.
- [ ] A conflict keeps the user's input and offers to reload; overwriting is confirmed.
- [ ] No failure path clears the form or closes its dialog.
