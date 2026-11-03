# Forms

> **Goal:** the user fills the form in one pass, knows what each field wants, fixes every error without losing input, and knows the result.
> **Sources:** Primer [Forms](https://primer.style/product/ui-patterns/forms/), [FormControl](https://primer.style/product/components/form-control/), [TextInput](https://primer.style/product/components/text-input/guidelines/), [Textarea](https://primer.style/product/components/textarea/guidelines/), [Saving](https://primer.style/product/ui-patterns/saving/), [Content](https://primer.style/product/getting-started/foundations/content/) · Mantine [use-form](https://mantine.dev/llms/form-use-form.md), [Form validation](https://mantine.dev/llms/form-validation.md), [Form validators](https://mantine.dev/llms/form-validators.md), [Form errors](https://mantine.dev/llms/form-errors.md), [Form status](https://mantine.dev/llms/form-status.md), [Focus first invalid input](https://mantine.dev/llms/q-focus-first-input-with-error.md), [Input](https://mantine.dev/llms/core-input.md), [TextInput](https://mantine.dev/llms/core-text-input.md), [Textarea](https://mantine.dev/llms/core-textarea.md), [PasswordInput](https://mantine.dev/llms/core-password-input.md), [NumberInput](https://mantine.dev/llms/core-number-input.md), [Fieldset](https://mantine.dev/llms/core-fieldset.md), [Alert](https://mantine.dev/llms/core-alert.md), [Button](https://mantine.dev/llms/core-button.md)

## When

- Use when the user enters or edits data and submits it: create, edit settings, sign in, invite.
- Not for choosing which input fits a choice → see [selection](../decisions/selection.md).
- Not for Save vs autosave, save states and unsaved changes → see [saving](saving.md).
- Not for messages outside the fields (toasts, banners) → see [feedback](../decisions/feedback.md).
- Not for multi-step wizards → see [navigation](../decisions/navigation.md) (`Stepper`); each step is still a form.
- Not for submit actions that delete or overwrite → see [destructive actions](../flows/destructive-actions.md).

## Anatomy

1. **Form-level error** (only after a failed submit): one `Alert color="red"` at the top of the form.
2. **Section** (long forms): `Fieldset legend="…"` or a `Title order={3}` above a group of related fields.
3. **Label**: visible, above the input, sentence case, no colon, about 3 words ("Server name").
4. **Required mark**: Mantine's asterisk from `required`.
5. **Input**: sized to the expected value.
6. **Description** (optional): `description`, a short hint that adds new information (format, where it is used).
7. **Error**: `error`, under the input, says what is wrong and how to fix it.
8. **Actions**: the submit `Button type="submit"` named with its action ("Create server"), then Cancel.
   On a page: bottom-left, Cancel to the right of submit. In a dialog: bottom-right, Cancel to the left.

## States

| State | What the user sees | Mantine |
| --- | --- | --- |
| Pristine | Defaults filled where safe; no errors | `useForm({ initialValues })` |
| Editing | No errors while the user types in a field they have not finished | default: validate on submit only |
| Submitted with errors | Errors under each invalid field; focus on the first one | `form.onSubmit(onValid, onInvalid)` + `form.getInputNode(path)?.focus()` |
| Fixing | The error goes away as soon as the value changes; checked again on blur | `clearInputErrorOnChange` (default `true`), `validateInputOnBlur: true` |
| Submitting | The submit button is busy; values stay visible | `form.submitting` + `Button loading` |
| Server rejected | Field errors from the server under their fields, others in a form-level `Alert`; all input kept | `form.setErrors({...})`, `form.setFieldError(path, msg)` |
| Saved | The result, per [saving](saving.md) | — |
| Cannot save now (read-only, outage) | Fields stay; an `Alert` says why saving is not possible | see [degraded experiences](degraded-experiences.md) |

## Rules

**Do**

- Give every input a visible `label`. Use `aria-label` only where the context is visual and obvious (a search box
  with a search icon), never in a form.
- Put help in `description`. Keep it short; do not repeat the label or the error.
- Mark required fields with `required` (asterisk + `required` attribute) and add `noValidate` to the `<form>` so the
  browser's own bubbles do not replace Mantine's messages. Validate the same fields in `validate`.
- Pick one convention per product: mark required fields (default), or mark optional ones with "(optional)" in the
  label when most fields are required. Never mix both. A sign-in form may skip marks.
- Lay fields out in one column, in a logical order: most important first, related fields together.
- Group related fields under a `Fieldset` legend or a heading. Split long forms into sections; put rare options
  behind a disclosure ([progressive disclosure](progressive-disclosure.md)).
- Size inputs to the expected value: short values (postal code, port, PIN) get a narrow input with `maw`; free text
  fills the column; long text uses `Textarea` with `autosize` and `minRows`.
- Use the right input and attributes: `NumberInput` for amounts, `type="email"`, `autoComplete` (`"email"`,
  `"current-password"`, `"new-password"`), `inputMode` for numeric codes.
- Validate on submit. Then let the error clear on change and re-validate on blur (`validateInputOnBlur: true`).
  Validate on every keystroke only for fields where live feedback helps (password rules, name availability):
  `validateInputOnChange: ['username']`.
- Write errors that say what is wrong and how to fix it: "Enter a port between 1 and 65535", not "Invalid value".
  See [writing](../writing.md).
- On a failed submit, move focus to the first invalid field. With 3 or more errors in a long form, also show a
  form-level `Alert` that names them.
- Map server errors to their fields (`form.setErrors`); show the rest in a form-level `Alert`. Keep every value.
- Show the submit as busy with `loading={form.submitting}` (it also blocks a second click).
- Put secrets in `PasswordInput`. For a stored secret, show that one is set and offer "Replace"; do not load it back.
- Let Enter submit: one `Button type="submit"` inside the `<form>`.

**Don't**

- Don't use a placeholder as the label or for information the user needs. It disappears on typing and is low
  contrast. A placeholder may show an example value ("e.g. eu-west-1").
- Don't show an error before the user has finished with a field (on the first keystroke or on focus).
- Don't disable the submit button to signal invalid or unchanged input; it cannot be focused and does not say why.
- Don't clear or reset the form after an error, and don't close the dialog that holds it.
- Don't lay fields out in two columns to save space (a first and last name pair is fine).
- Don't put an error under each radio or checkbox of a group; put one on the group.
- Don't pre-fill a password or token field with a stored or fake value.
- Don't label the submit "Submit" or "OK".

## Recipe

```tsx
import { useState } from 'react';
import { Alert, Button, Fieldset, Group, NumberInput, PasswordInput, Stack, TextInput } from '@mantine/core';
import { hasLength, isInRange, isNotEmpty, useForm } from '@mantine/form';
import { IconAlertCircle } from '@tabler/icons-react';

export function CreateServerForm({ onCancel }: { onCancel: () => void }) {
  const [formError, setFormError] = useState<string | null>(null);
  const form = useForm({
    mode: 'uncontrolled',
    initialValues: { name: '', port: 2456, password: '' },
    validateInputOnBlur: true,
    validate: {
      name: hasLength({ min: 3, max: 40 }, 'Enter a name of 3 to 40 characters'),
      port: isInRange({ min: 1024, max: 65535 }, 'Enter a port between 1024 and 65535'),
      password: isNotEmpty('Enter a password for players to join'),
    },
  });

  const submit = async (values: typeof form.values) => {
    setFormError(null);
    const result = await createServer(values); // placeholder: returns { ok, fieldErrors, message }
    if (!result.ok) {
      form.setErrors(result.fieldErrors); // e.g. { name: 'A server with this name already exists' }
      setFormError(result.message);
    }
  };

  const focusFirstError = (errors: typeof form.errors) => {
    const first = Object.keys(errors)[0];
    if (first) form.getInputNode(first)?.focus();
  };

  return (
    <form noValidate onSubmit={form.onSubmit(submit, focusFirstError)}>
      <Stack gap="md">
        {formError && (
          <Alert color="red" icon={<IconAlertCircle />} title="Couldn't create the server">
            {formError}
          </Alert>
        )}
        <Fieldset legend="Server">
          <Stack gap="sm">
            <TextInput label="Server name" required key={form.key('name')} {...form.getInputProps('name')} />
            <NumberInput label="Port" description="Players connect on this port." required maw="12ch"
              key={form.key('port')} {...form.getInputProps('port')} />
            <PasswordInput label="Join password" required autoComplete="new-password"
              key={form.key('password')} {...form.getInputProps('password')} />
          </Stack>
        </Fieldset>
        <Group gap="sm">
          <Button type="submit" loading={form.submitting}>Create server</Button>
          <Button variant="default" onClick={onCancel}>Cancel</Button>
        </Group>
      </Stack>
    </form>
  );
}
```

`form.getInputNode` works only for inputs that spread `form.getInputProps`. The first key of `errors` follows the
order of `validate`; keep that order the same as the fields on screen.

## Checklist

- [ ] Every input has a visible label above it; no placeholder stands in for a label.
- [ ] Required (or optional) fields follow one convention; required fields use `required` and the form has `noValidate`.
- [ ] The form is one column, grouped into labelled sections when long.
- [ ] Inputs are sized to their expected value.
- [ ] No error appears before the user finishes a field; after submit, errors clear on change and re-check on blur.
- [ ] Every error says what is wrong and how to fix it, under its field.
- [ ] A failed submit moves focus to the first invalid field.
- [ ] Server errors appear under their fields or in one form-level `Alert`; no input is lost.
- [ ] The submit button stays enabled, is named with its action, and shows `loading` while submitting.
- [ ] Secrets use `PasswordInput` and are never pre-filled.
