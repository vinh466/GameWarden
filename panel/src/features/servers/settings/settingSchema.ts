import type { TFunction } from 'i18next';
import { z } from 'zod';
import type { AdapterSetting } from '@/api/generated';

// Core's SettingsValidation.MaxValueLength.
const maxValueLength = 1024;

/**
 * Why a setting's text does not fit, as Core checks it (SettingDefinition.Check); null when it fits. Core stays the
 * judge, this only answers sooner. `keepEmpty`: empty is allowed (a secret left unchanged while editing).
 */
export function settingProblem(
  setting: AdapterSetting,
  value: string,
  t: TFunction,
  keepEmpty = false
): string | null {
  if (value === '') {
    return setting.required && !keepEmpty ? t('settings.required') : null;
  }

  if (setting.type === 'bool' || setting.type === 'enum') {
    const allowed =
      setting.type === 'bool' ? ['true', 'false'] : setting.options.map((option) => option.value);
    return allowed.includes(value) ? null : t('settings.pickOne');
  }

  if (setting.type === 'int') {
    if (!/^-?\d+$/.test(value)) {
      return t('settings.wholeNumber');
    }

    const number = Number(value);
    const outside =
      (setting.min !== null && number < setting.min) ||
      (setting.max !== null && number > setting.max);
    return outside
      ? t('settings.range', { min: setting.min ?? '…', max: setting.max ?? '…' })
      : null;
  }

  if (setting.type === 'number') {
    if (
      !/^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$/.test(value) ||
      !Number.isFinite(Number(value))
    ) {
      return t('settings.number');
    }

    const number = Number(value);
    const outside =
      (setting.min !== null && number < setting.min) ||
      (setting.max !== null && number > setting.max);
    return outside
      ? t('settings.range', { min: setting.min ?? '…', max: setting.max ?? '…' })
      : null;
  }

  if (setting.type === 'list') {
    const items = value.split(',').map((item) => item.trim());
    const unknown = items.find(
      (item) => item === '' || !setting.options.some((option) => option.value === item)
    );
    return unknown !== undefined ? t('settings.listItem', { value: unknown || '∅' }) : null;
  }

  if (value.length < (setting.minLength ?? 0)) {
    return t('settings.tooShort', { count: setting.minLength ?? 0 });
  }

  if (value.length > (setting.maxLength ?? maxValueLength)) {
    return t('settings.tooLong', { count: setting.maxLength ?? maxValueLength });
  }

  const malformed =
    /\p{Cc}/u.test(value) || (setting.pattern !== null && !new RegExp(setting.pattern).test(value));
  return malformed ? t('settings.invalid') : null;
}

/**
 * The zod shape of the settings form for these fields, for `schemaResolver` (panel/CONTRIBUTING: a form). `editing`:
 * an empty secret means "unchanged".
 */
export function settingValuesSchema(fields: AdapterSetting[], t: TFunction, editing: boolean) {
  const rule = (setting: AdapterSetting) =>
    z.string().superRefine((value, context) => {
      const problem = settingProblem(setting, value, t, editing && setting.type === 'secret');
      if (problem) {
        context.addIssue({ code: 'custom', message: problem });
      }
    });
  const of = (secret: boolean) =>
    z.object(
      Object.fromEntries(
        fields
          .filter((setting) => (setting.type === 'secret') === secret)
          .map((setting) => [setting.key, rule(setting)])
      )
    );
  return { settings: of(false), secrets: of(true) };
}

/** Starting values: the stored value, else the default, else empty; secrets always empty. */
export function initialSettingValues(
  fields: AdapterSetting[],
  stored: Record<string, string> = {}
) {
  return {
    settings: Object.fromEntries(
      fields
        .filter((setting) => setting.type !== 'secret')
        .map((setting) => [setting.key, stored[setting.key] ?? setting.default ?? ''])
    ),
    secrets: Object.fromEntries(
      fields.filter((setting) => setting.type === 'secret').map((setting) => [setting.key, ''])
    ),
  };
}
