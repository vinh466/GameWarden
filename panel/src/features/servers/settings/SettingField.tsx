import { IconAlertTriangle } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Badge,
  Group,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  Switch,
  TextInput,
} from '@mantine/core';
import type { GetInputPropsReturnType } from '@mantine/form';
import type { AdapterSetting } from '@/api/generated';
import { localized, settingLabel } from './settingText';

/**
 * One setting of an adapter's schema (04 §5.1) as its Mantine input: text, number, switch, select or a write-only
 * password. Values stay text, as Core stores them. A warning shows below the input whatever its value.
 * `secretSet`: while editing, whether the secret has a value already.
 */
export function SettingField({
  setting,
  input,
  secretSet,
}: {
  setting: AdapterSetting;
  input: GetInputPropsReturnType;
  secretSet?: boolean;
}) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage;
  const label = settingLabel(setting, language);
  const description =
    localized(setting.help, language) ??
    (setting.type === 'secret' && secretSet !== undefined ? t('settings.secretsHint') : undefined);
  const warning = localized(setting.warning, language);
  // The rest of getInputProps (error, focus and blur handlers, data-path for getInputNode) goes on every input.
  const { value: raw, onChange: change, ...rest } = input;
  const value = typeof raw === 'string' ? raw : '';
  const onChange = change as (value: string) => void;
  // While editing, a secret's Set / Not set badge says what the asterisk would, and empty keeps it.
  const common = {
    ...rest,
    label,
    description,
    withAsterisk: setting.required && secretSet === undefined,
  };

  const field = (() => {
    switch (setting.type) {
      case 'bool':
        return (
          <Switch
            {...common}
            checked={value === 'true'}
            onChange={(event) => onChange(event.currentTarget.checked ? 'true' : 'false')}
          />
        );
      case 'int':
        return (
          <NumberInput
            {...common}
            value={value}
            min={setting.min ?? undefined}
            max={setting.max ?? undefined}
            allowDecimal={false}
            maw={240}
            onChange={(next) => onChange(String(next))}
          />
        );
      case 'enum':
        return (
          <Select
            {...common}
            value={value}
            allowDeselect={false}
            data={setting.options.map((option) => ({
              value: option.value,
              label: localized(option.label, language) ?? option.value,
            }))}
            onChange={(next) => onChange(next ?? '')}
          />
        );
      case 'secret':
        return (
          <PasswordInput
            {...common}
            label={
              secretSet === undefined ? (
                label
              ) : (
                <Group gap={6} component="span">
                  {label}
                  <Badge size="xs" color={secretSet ? 'green' : 'red'} component="span">
                    {secretSet ? t('settings.secretSet') : t('settings.secretNotSet')}
                  </Badge>
                </Group>
              )
            }
            placeholder={secretSet === undefined ? undefined : t('settings.secretPlaceholder')}
            autoComplete="new-password"
            value={value}
            onChange={(event) => onChange(event.currentTarget.value)}
          />
        );
      default:
        return (
          <TextInput
            {...common}
            value={value}
            onChange={(event) => onChange(event.currentTarget.value)}
          />
        );
    }
  })();

  return warning ? (
    <Stack gap={6}>
      {field}
      <Alert color="yellow" variant="light" icon={<IconAlertTriangle aria-hidden />} p="xs">
        {warning}
      </Alert>
    </Stack>
  ) : (
    field
  );
}
