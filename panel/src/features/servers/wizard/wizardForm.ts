import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { schemaResolver, useForm, type UseFormReturnType } from '@mantine/form';
import type { AdapterSetting, AdapterSummary } from '@/api/generated';
import { settingValuesSchema } from '../settings/settingSchema';
import { settingPath, type SettingValues } from '../settings/settingText';
import { groupSection } from '../settings/useSettingSections';

export interface WizardValues extends SettingValues {
  displayName: string;
  id: string;
}

export type WizardForm = UseFormReturnType<WizardValues>;

/** Shown in place of the adapter's groups until a game is picked. */
export const settingsPlaceholder = 'settings';

/** What the wizard asks of an adapter: its required settings and those marked `wizard` (04 §5.1). */
export function wizardFields(adapter: AdapterSummary | null): AdapterSetting[] {
  return adapter?.settings.filter((setting) => setting.wizard) ?? [];
}

/** Section ids in order (docs/11 §4): game, name, the adapter's groups with something to ask, review. */
export function wizardSectionIds(adapter: AdapterSummary | null): string[] {
  const fields = wizardFields(adapter);
  const groups = adapter
    ? adapter.settingGroups
        .filter((group) => fields.some((setting) => setting.group === group.id))
        .map((group) => groupSection(group.id))
    : [settingsPlaceholder];
  return ['game', 'name', ...groups, 'review'];
}

/** One schema for the whole wizard, built for the picked game; each section validates only its own fields. */
export function useWizardForm(fields: AdapterSetting[]): WizardForm {
  const { t } = useTranslation();
  return useForm<WizardValues>({
    mode: 'controlled',
    initialValues: { displayName: '', id: '', settings: {}, secrets: {} },
    validate: schemaResolver(
      z.object({
        displayName: z.string().trim().min(1, t('settings.required')).max(80),
        id: z.string().regex(/^[a-z0-9][a-z0-9-]{1,62}$/, t('wizard.idHint')),
        ...settingValuesSchema(fields, t, false),
      }),
      { sync: true }
    ),
  });
}

/** The form paths a section owns. */
export function sectionFields(section: string, adapter: AdapterSummary | null): string[] {
  if (section === 'name') {
    return ['displayName', 'id'];
  }

  return wizardFields(adapter)
    .filter((setting) => groupSection(setting.group) === section)
    .map(settingPath);
}

/** Validates every field of a section, so all its errors show at once; true when it has none. */
export function validateSection(form: WizardForm, paths: string[]): boolean {
  return paths.map((path) => form.validateField(path).hasError).every((hasError) => !hasError);
}
