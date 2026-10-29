import { useTranslation } from 'react-i18next';
import { Stack, Text } from '@mantine/core';
import type { FormErrors, GetInputPropsReturnType } from '@mantine/form';
import type { AdapterSetting, AdapterSettingGroup } from '@/api/generated';
import type { FormSection } from '@/components/FormSections';
import { HelpButton } from '@/components/help/HelpButton';
import { SettingField } from './SettingField';
import {
  groupLabel,
  localized,
  settingPath,
  settingsSummary,
  type SettingValues,
} from './settingText';

/** The section id of an adapter's settings group; also its key in the adapter's help.json (04 §5.3). */
export const groupSection = (id: string) => `group:${id}`;

/**
 * One FormSection per settings group of an adapter (04 §5, docs/11 D10), in the adapter's order, each with the
 * fields given for it; groups without fields are left out. The new server wizard passes the fields it asks, the
 * Settings tab all of them. Groups and fields with a topic in the adapter's help.json get a help button.
 */
export function useSettingSections({
  adapterId,
  groups,
  fields,
  values,
  errors,
  input,
  secretsSet,
}: {
  /** The adapter whose help.json the help buttons open; none without it. */
  adapterId?: string;
  groups: AdapterSettingGroup[];
  fields: AdapterSetting[];
  values: SettingValues;
  errors: FormErrors;
  input: (path: string) => GetInputPropsReturnType;
  secretsSet?: Record<string, boolean>;
}): FormSection[] {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage;
  return groups.flatMap((group) => {
    const own = fields.filter((setting) => setting.group === group.id);
    if (own.length === 0) {
      return [];
    }

    const help = localized(group.help, language);
    const title = groupLabel(group, language, t);
    return [
      {
        id: groupSection(group.id),
        title,
        summary: settingsSummary(own, values, language, t),
        invalid: own.some((setting) => !!errors[settingPath(setting)]),
        action:
          group.hasHelp && adapterId ? (
            <HelpButton
              label={title}
              help={{ title, adapter: { id: adapterId, key: groupSection(group.id) } }}
            />
          ) : undefined,
        children: (
          <Stack gap="sm" maw={560}>
            {help && (
              <Text size="sm" c="dimmed">
                {help}
              </Text>
            )}
            {own.map((setting) => (
              <SettingField
                key={setting.key}
                setting={setting}
                adapterId={adapterId}
                input={input(settingPath(setting))}
                secretSet={secretsSet?.[setting.key]}
              />
            ))}
          </Stack>
        ),
      },
    ];
  });
}
