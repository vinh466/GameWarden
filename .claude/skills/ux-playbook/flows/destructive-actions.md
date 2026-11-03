# Destructive actions

> **Goal:** the user removes or overwrites exactly what they meant to, knows what it cost, and can recover from mistakes where the system allows it.
> **Sources:** Primer [ConfirmationDialog guidelines](https://primer.style/product/components/confirmation-dialog/guidelines/), [Dialog guidelines](https://primer.style/product/components/dialog/guidelines/), [Button](https://primer.style/product/components/button/), [Saving](https://primer.style/product/ui-patterns/saving/), [Focus management](https://primer.style/accessibility/design-guidance/focus-management/) · Mantine [Modals manager](https://mantine.dev/llms/x-modals.md), [Modal](https://mantine.dev/llms/core-modal.md), [Notifications](https://mantine.dev/llms/x-notifications.md), [Menu](https://mantine.dev/llms/core-menu.md), [Button](https://mantine.dev/llms/core-button.md), [TextInput](https://mantine.dev/llms/core-text-input.md)

## When

- Use for delete, remove, reset, revoke, overwrite, discard, "leave team", and any change that cannot be reversed,
  one item or many (bulk).
- Not for routine, reversible edits (rename, move, toggle): save them normally, without a dialog.
- Not for discarding unsaved form changes on navigation: see [saving](../patterns/saving.md).

## Severity decides the protection

Pick the row that matches the worst consequence. A business rule ("deleting needs approval") always wins over this table.

| Severity | Typical action | Protection |
| --- | --- | --- |
| Low: reversible, or easy to redo | Archive, remove from a list, move to trash, delete a draft | Do it at once, then a notification with **Undo** |
| Medium: irreversible, limited scope | Delete one server, revoke one token, reset one setting | Confirmation dialog |
| High: irreversible, large scope or affects other people | Delete a project, remove a member, wipe a data set, bulk delete | Type-to-confirm dialog (type the object's name) |
| Critical: irreversible, whole account or organisation, money or legal impact | Delete organisation, close account, purge all backups | Type-to-confirm + a warning `Alert` listing what is lost + offer an export or backup first |

- Prefer undo over confirmation whenever the backend can keep the data for a few seconds (soft delete, trash).
- Do not confirm routine reversible actions. Users who see a dialog every time stop reading it (confirmation fatigue).

## Steps

1. **Trigger.** The user picks a red action placed apart from routine ones: last item of a "More" `Menu`, after a
   `Menu.Divider`, with `color="red"` and a trash icon; or a red `Button` in a separate "Danger zone" section.
   Never put a destructive button next to the primary action or make it the primary action of a page.
   The label is verb + object: "Delete server". See [actions](../decisions/actions.md).
2. **Protection by severity** (table above):
   - **Undo:** apply the change in the UI at once, show a notification "Server archived" with an **Undo** button,
     and commit after the notification closes (or use a soft delete the backend can restore).
   - **Confirm:** open `modals.openConfirmModal`. Title names the action and object ("Delete server web-01?").
     Body states the consequence ("This deletes the server and its 12 backups. This can't be undone.").
     `labels.confirm` repeats the action ("Delete server"), `labels.cancel` is "Cancel",
     `confirmProps: { color: 'red' }`.
   - **Type-to-confirm:** a custom `Modal` with a `TextInput` labelled "Type web-01 to confirm". The confirm button
     stays enabled; a click with a mismatch shows the input's `error`.
3. **Focus.** Initial focus never lands on the destructive button. `openConfirmModal` focuses the first focusable
   element (the close button); in a custom modal put `data-autofocus` on the `TextInput`.
4. **Pending.** The confirm button shows `loading` and the dialog stays open until the server answers; Cancel and
   close do nothing while pending. With `openConfirmModal` (it closes on confirm), show a loading notification and
   update it with the result.
5. **Result.** Close the dialog, remove the item from the UI, and show a short notification in past tense:
   "Server web-01 deleted". Move focus to the next item, else the previous one, else the list heading.
   If the user was on the deleted object's own page, navigate to its parent list first, then notify.
6. **Bulk.** State the count everywhere: "Delete 3 servers?", button "Delete 3 servers", result "3 servers
   deleted". If some fail, say how many and which: "2 servers deleted. 1 couldn't be deleted: web-03."

## Failures

| Failure | What the user sees | Recovery |
| --- | --- | --- |
| Server error during confirm | Custom modal: stays open with a red `Alert` inside. `openConfirmModal`: the loading notification turns red. "Couldn't delete server web-01. Try again." | Confirm again or Cancel; nothing was removed |
| Undo-style action fails on commit | The item reappears; a red notification "Couldn't archive web-01" | Retry from the item's menu |
| Undo clicked after the commit | Notification "Too late to undo. The server is in Trash." (or the restore path) | Restore from trash, if the product has one |
| Typed name does not match | Input error "Type web-01 exactly to confirm" | Fix the text; focus stays in the input |
| No permission | Action visible but explains why: tooltip or dialog "Only admins can delete servers" | Ask an admin; see [feedback](../decisions/feedback.md) |
| Object already gone (deleted elsewhere) | Notification "web-01 was already deleted"; the list refreshes | Nothing to do |
| Bulk partly fails | "2 servers deleted. 1 couldn't be deleted: web-03." Failed items stay selected | Retry the remaining items |

## Rules

- Say exactly what will be lost: the object's name, what goes with it (backups, members, history), and whether it
  can be undone. Never "Are you sure?" alone. Wording: [writing](../writing.md).
- The confirm button repeats the action and object. Never "OK", "Yes" or "Confirm".
- Use a danger colour (`color="red"`) on the trigger and the confirm button only, never on Cancel.
- Keep the Undo notification open long enough to act on: at least 8 seconds (`autoClose: 8000`). A timed Undo is a
  convenience; for High and Critical actions it is not enough on its own.
- Never lose the user's typed input on a failed attempt; keep the dialog open with the error.
- Which overlay: [overlays](../decisions/overlays.md). Messages after the action: [feedback](../decisions/feedback.md).
- Keyboard: Esc and Cancel close the dialog when not pending; focus returns to the trigger (Mantine does this), or
  to a neighbour when the trigger was deleted.

## Recipe

Undo (Low). `archive` and `restore` are your handlers; `restore` brings the item back.

```tsx
archive(server);
const id = notifications.show({
  message: (
    <Group justify="space-between">
      <Text size="sm">Server {server.name} archived</Text>
      <Button size="compact-sm" variant="subtle" onClick={() => { restore(server); notifications.hide(id); }}>Undo</Button>
    </Group>
  ),
  autoClose: 8000,
});
```

Confirmation dialog (Medium). Needs `ModalsProvider` and `Notifications` mounted once at the app root.

```tsx
import { Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';

export function confirmDeleteServer(name: string, deleteServer: () => Promise<void>) {
  modals.openConfirmModal({
    title: `Delete server ${name}?`,
    children: <Text size="sm">This deletes the server and all its backups. This can't be undone.</Text>,
    labels: { confirm: 'Delete server', cancel: 'Cancel' },
    confirmProps: { color: 'red' },
    onConfirm: async () => {
      const id = notifications.show({ loading: true, message: `Deleting server ${name}…`, autoClose: false });
      try {
        await deleteServer();
        notifications.update({ id, loading: false, color: 'green', message: `Server ${name} deleted`, autoClose: 4000 });
      } catch {
        notifications.update({ id, loading: false, color: 'red', message: `Couldn't delete server ${name}. Try again.` });
      }
    },
  });
}
```

Type-to-confirm (High, Critical). The button stays enabled and validates on click.

```tsx
import { useState } from 'react';
import { Alert, Button, Group, Modal, Stack, Text, TextInput } from '@mantine/core';
import { IconAlertTriangle } from '@tabler/icons-react';

type Props = { opened: boolean; onClose: () => void; name: string; onDelete: () => Promise<void> };

export function DeleteProjectModal({ opened, onClose, name, onDelete }: Props) {
  const [value, setValue] = useState('');
  const [mismatch, setMismatch] = useState(false);
  const [failed, setFailed] = useState(false);
  const [pending, setPending] = useState(false);

  const reset = () => {
    setValue('');
    setMismatch(false);
    setFailed(false);
    onClose();
  };
  const close = () => { if (!pending) reset(); };

  const confirm = async () => {
    if (value !== name) return setMismatch(true);
    setPending(true);
    setFailed(false);
    try {
      await onDelete();
      reset();
    } catch {
      setFailed(true);
    } finally {
      setPending(false);
    }
  };

  return (
    <Modal opened={opened} onClose={close} title={`Delete project ${name}?`}>
      <Stack gap="md">
        <Text size="sm">This deletes the project, its 4 servers and all backups. This can't be undone.</Text>
        {failed && (
          <Alert color="red" icon={<IconAlertTriangle />}>Couldn't delete the project. Try again.</Alert>
        )}
        <TextInput
          data-autofocus
          label={`Type ${name} to confirm`}
          value={value}
          onChange={(event) => { setValue(event.currentTarget.value); setMismatch(false); }}
          error={mismatch ? `Type ${name} exactly to confirm` : undefined}
          autoComplete="off"
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={close} disabled={pending}>Cancel</Button>
          <Button color="red" onClick={confirm} loading={pending}>Delete project</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
```

## Checklist

- [ ] The destructive trigger is red, labelled verb + object, and placed apart from routine actions.
- [ ] Protection matches the severity table: undo, confirm, or type-to-confirm; routine reversible actions have none.
- [ ] The dialog title names the action and the object; the body states what is lost and whether it can be undone.
- [ ] The confirm button repeats the action ("Delete server"), uses `color="red"`, and shows `loading` while pending.
- [ ] Initial focus is not on the destructive button; Esc and Cancel work when not pending.
- [ ] Type-to-confirm says exactly what to type and shows an input error on mismatch.
- [ ] Bulk actions state the count in title, button and result; partial failures name the failed items.
- [ ] After success: notification, item gone from the UI, focus on a logical neighbour, navigation away if needed.
- [ ] After failure: nothing is lost, the dialog stays open with an error, the user can retry.
- [ ] Undo notifications stay at least 8 seconds and the Undo button is reachable by keyboard.
