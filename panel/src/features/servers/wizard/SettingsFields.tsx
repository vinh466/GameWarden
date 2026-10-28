import { useTranslation } from 'react-i18next';
import { PasswordInput, Stack, Text, TextInput } from '@mantine/core';
import type { WizardForm } from './wizardForm';

/** Step 3: the adapter's required settings, and its secrets as write-only inputs (docs/11 §10 rule 7). */
export function SettingsFields({ form }: { form: WizardForm }) {
  const { t } = useTranslation();
  const settings = Object.keys(form.values.settings);
  const secrets = Object.keys(form.values.secrets);
  return (
    <Stack gap="sm" maw={480}>
      {settings.length + secrets.length === 0 && (
        <Text size="sm" c="dimmed">
          {t('wizard.noSettings')}
        </Text>
      )}
      {settings.map((name) => (
        <TextInput
          key={name}
          label={name}
          withAsterisk
          {...form.getInputProps(`settings.${name}`)}
        />
      ))}
      {secrets.map((name) => (
        <PasswordInput
          key={name}
          label={name}
          description={t('settings.secretsHint')}
          withAsterisk
          autoComplete="new-password"
          {...form.getInputProps(`secrets.${name}`)}
        />
      ))}
    </Stack>
  );
}
