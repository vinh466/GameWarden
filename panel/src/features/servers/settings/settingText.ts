import type { TFunction } from 'i18next';
import type { AdapterSetting, AdapterSettingGroup } from '@/api/generated';

/** A setting's or a group's text per language, as the adapter gives it (04 §5). */
export type Localized = { [language: string]: string } | null | undefined;

/** Values of the settings form: settings as text, secrets write-only (docs/11 §10 rule 7). */
export interface SettingValues {
  settings: Record<string, string>;
  secrets: Record<string, string>;
}

/** The text in the panel's language, else English (docs/11 D1). */
export function localized(text: Localized, language: string | undefined): string | undefined {
  if (!text) {
    return undefined;
  }

  return (language ? text[language] : undefined) ?? text.en ?? Object.values(text)[0];
}

export function settingLabel(setting: AdapterSetting, language: string | undefined) {
  return localized(setting.label, language) ?? setting.key;
}

export function groupLabel(group: AdapterSettingGroup, language: string | undefined, t: TFunction) {
  return localized(group.label, language) ?? t('settings.title');
}

/** The form path of a setting: secrets apart, so they never mix with the values Core returns. */
export function settingPath(setting: AdapterSetting) {
  return setting.type === 'secret' ? `secrets.${setting.key}` : `settings.${setting.key}`;
}

/**
 * One line for a closed section: only what differs from the defaults, so a glance shows what was changed
 * ("Preset: Hard · Raids: None · No map"); a secret only as set.
 */
export function settingsSummary(
  fields: AdapterSetting[],
  values: SettingValues,
  language: string | undefined,
  t: TFunction
): string {
  const parts = fields.flatMap((setting) => {
    const label = settingLabel(setting, language);
    if (setting.type === 'secret') {
      return values.secrets[setting.key] ? [`${label}: ••••`] : [];
    }

    const value = values.settings[setting.key] ?? '';
    if (value === '' || value === (setting.default ?? '')) {
      return [];
    }

    if (setting.type === 'bool') {
      return [value === 'true' ? label : `${label}: ${t('settings.off')}`];
    }

    const option = setting.options.find((candidate) => candidate.value === value);
    const shown = option ? (localized(option.label, language) ?? option.value) : value;
    return [`${label}: ${shown}`];
  });
  return parts.length > 0 ? parts.join(' · ') : t('settings.allDefault');
}
