import { useTranslation } from 'react-i18next';
import { Stack, TextInput } from '@mantine/core';
import { HelpField } from '@/components/help/HelpLabel';
import { slug } from './slug';
import type { WizardForm } from './wizardForm';

/** Section 2: display name, and the id derived from it until the Owner edits the id. */
export function NameFields({ form }: { form: WizardForm }) {
  const { t } = useTranslation();
  return (
    <Stack gap="sm" maw={480}>
      <HelpField label={t('wizard.displayName')} help={{ topicId: 'wizard.name' }} required>
        {(id) => (
          <TextInput
            id={id}
            description={t('wizard.displayNameHint')}
            {...form.getInputProps('displayName')}
            onChange={(event) => {
              const value = event.currentTarget.value;
              if (!form.isDirty('id') || form.values.id === slug(form.values.displayName)) {
                form.setFieldValue('id', slug(value));
              }

              form.setFieldValue('displayName', value);
            }}
          />
        )}
      </HelpField>
      <HelpField label={t('wizard.id')} help={{ topicId: 'wizard.id' }} required>
        {(id) => (
          <TextInput
            id={id}
            description={t('wizard.idHint')}
            ff="monospace"
            {...form.getInputProps('id')}
          />
        )}
      </HelpField>
    </Stack>
  );
}
