import { useTranslation } from 'react-i18next';
import { Stack, TextInput } from '@mantine/core';
import { slug } from './slug';
import type { WizardForm } from './wizardForm';

/** Step 2: display name, and the id derived from it until the Owner edits the id. */
export function NameFields({ form }: { form: WizardForm }) {
  const { t } = useTranslation();
  return (
    <Stack gap="sm" maw={480}>
      <TextInput
        label={t('wizard.displayName')}
        description={t('wizard.displayNameHint')}
        withAsterisk
        {...form.getInputProps('displayName')}
        onChange={(event) => {
          const value = event.currentTarget.value;
          if (!form.isDirty('id') || form.values.id === slug(form.values.displayName)) {
            form.setFieldValue('id', slug(value));
          }

          form.setFieldValue('displayName', value);
        }}
      />
      <TextInput
        label={t('wizard.id')}
        description={t('wizard.idHint')}
        withAsterisk
        ff="monospace"
        {...form.getInputProps('id')}
      />
    </Stack>
  );
}
