import { useId, type ReactNode } from 'react';
import { Group, Input } from '@mantine/core';
import { HelpButton } from './HelpButton';
import type { HelpRequest } from './types';

/**
 * A field's label with its help button beside it. Mantine's own label would put the button inside the `<label>`
 * element, which changes the input's accessible name (and a button may not sit in a label); so the row is built from
 * `Input.Label`, as Mantine's docs suggest for custom layouts ("Input.Label, Input.Description and Input.Error").
 */
export function HelpLabel({
  label,
  htmlFor,
  required,
  help,
}: {
  label: string;
  htmlFor: string;
  required?: boolean;
  help: HelpRequest;
}) {
  return (
    <Group gap={4} wrap="nowrap" align="center">
      <Input.Label htmlFor={htmlFor} required={required}>
        {label}
      </Input.Label>
      <HelpButton label={label} help={help} />
    </Group>
  );
}

/**
 * A Mantine input under a `HelpLabel`: the input gets the id the label points to, and no `label` of its own.
 *
 * ```tsx
 * <HelpField label={t('wizard.id')} help={{ topicId: 'wizard.id' }} required>
 *   {(id) => <TextInput id={id} description={t('wizard.idHint')} {...form.getInputProps('id')} />}
 * </HelpField>
 * ```
 */
export function HelpField({
  label,
  required,
  help,
  children,
}: {
  label: string;
  required?: boolean;
  help: HelpRequest;
  children: (id: string) => ReactNode;
}) {
  const id = useId();
  return (
    <div>
      <HelpLabel label={label} htmlFor={id} required={required} help={help} />
      {children(id)}
    </div>
  );
}
