import { IconAlertTriangle } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Badge,
  Group,
  MultiSelect,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  Switch,
  TextInput,
} from '@mantine/core';
import type { GetInputPropsReturnType } from '@mantine/form';
import type { AdapterSetting } from '@/api/generated';
import { HelpButton } from '@/components/help/HelpButton';
import { HelpField } from '@/components/help/HelpLabel';
import type { HelpRequest } from '@/components/help/types';
import { localized, settingLabel } from './settingText';

/**
 * One setting of an adapter's schema (04 §5.1) as its Mantine input: text, number, switch, select or a write-only
 * password. Values stay text, as Core stores them. A warning shows below the input whatever its value.
 * `secretSet`: while editing, whether the secret has a value already. When the adapter's `help.json` has a topic for
 * the setting (`hasHelp`), a help button sits beside the label, outside the `<label>` (docs/12 §3).
 */
export function SettingField({
  setting,
  adapterId,
  input,
  secretSet,
}: {
  setting: AdapterSetting;
  adapterId?: string;
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
  const required = setting.required && secretSet === undefined;
  const content =
    setting.type === 'secret' && secretSet !== undefined ? (
      <Group gap={6} component="span">
        {label}
        {' ' /* the accessible name reads "Password Set", not "PasswordSet" */}
        <Badge size="xs" color={secretSet ? 'green' : 'red'} component="span">
          {secretSet ? t('settings.secretSet') : t('settings.secretNotSet')}
        </Badge>
      </Group>
    ) : (
      label
    );
  const help: HelpRequest | undefined =
    setting.hasHelp && adapterId
      ? {
          title: label,
          adapter: { id: adapterId, key: setting.key },
          defaultValue: setting.default ?? undefined,
        }
      : undefined;
  // A switch keeps its label beside it, the help button after both; other inputs get a label row with the button.
  const ownLabel = !help || setting.type === 'bool';
  const common = {
    ...rest,
    description,
    // A switch always has a value, so it has no asterisk (Switch would pass `withAsterisk` on to the DOM).
    ...(ownLabel ? { label: content } : {}),
    ...(ownLabel && setting.type !== 'bool' ? { withAsterisk: required } : {}),
  };

  const render = (id?: string) => {
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
            id={id}
            value={value}
            min={setting.min ?? undefined}
            max={setting.max ?? undefined}
            allowDecimal={false}
            maw={240}
            onChange={(next) => onChange(String(next))}
          />
        );
      case 'number':
        return (
          <NumberInput
            {...common}
            id={id}
            value={value}
            min={setting.min ?? undefined}
            max={setting.max ?? undefined}
            step={setting.step ?? undefined}
            allowDecimal
            maw={240}
            onChange={(next) => onChange(next === '' ? '' : String(next))}
          />
        );
      case 'enum':
        return (
          <Select
            {...common}
            id={id}
            value={value}
            allowDeselect={false}
            data={setting.options.map((option) => ({
              value: option.value,
              label: localized(option.label, language) ?? option.value,
            }))}
            onChange={(next) => onChange(next ?? '')}
          />
        );
      case 'list':
        return (
          <MultiSelect
            {...common}
            id={id}
            value={value === '' ? [] : value.split(',')}
            data={setting.options.map((option) => ({
              value: option.value,
              label: localized(option.label, language) ?? option.value,
            }))}
            clearable
            searchable
            onChange={(next) => onChange(next.join(','))}
          />
        );
      case 'secret':
        return (
          <PasswordInput
            {...common}
            id={id}
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
            id={id}
            value={value}
            onChange={(event) => onChange(event.currentTarget.value)}
          />
        );
    }
  };

  const field = !help ? (
    render()
  ) : ownLabel ? (
    <Group gap={4} wrap="nowrap" align="flex-start">
      {render()}
      <HelpButton label={label} help={help} />
    </Group>
  ) : (
    <HelpField label={label} content={content} required={required} help={help}>
      {render}
    </HelpField>
  );

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
