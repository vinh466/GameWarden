import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { schemaResolver, useForm, type UseFormReturnType } from '@mantine/form';

export interface WizardValues {
  displayName: string;
  id: string;
  settings: Record<string, string>;
  secrets: Record<string, string>;
}

export type WizardForm = UseFormReturnType<WizardValues>;

/** The wizard's sections in order (docs/11 §4). */
export const wizardSections = ['game', 'name', 'settings', 'review'] as const;

export type WizardSection = (typeof wizardSections)[number];

/** One schema for the whole wizard; each section validates only its own fields (`sectionFields`). */
export function useWizardForm(): WizardForm {
  const { t } = useTranslation();
  return useForm<WizardValues>({
    mode: 'controlled',
    initialValues: { displayName: '', id: '', settings: {}, secrets: {} },
    validate: schemaResolver(
      z.object({
        displayName: z.string().trim().min(1, t('settings.required')).max(80),
        id: z.string().regex(/^[a-z0-9][a-z0-9-]{1,62}$/, t('wizard.idHint')),
        settings: z.record(z.string(), z.string().min(1, t('settings.required')).max(1024)),
        secrets: z.record(z.string(), z.string().min(1, t('settings.required')).max(1024)),
      }),
      { sync: true }
    ),
  });
}

/** The form paths a section owns. */
export function sectionFields(section: WizardSection, values: WizardValues): string[] {
  if (section === 'name') {
    return ['displayName', 'id'];
  }

  if (section === 'settings') {
    return [
      ...Object.keys(values.settings).map((name) => `settings.${name}`),
      ...Object.keys(values.secrets).map((name) => `secrets.${name}`),
    ];
  }

  return [];
}

/** Validates every field of a section, so all its errors show at once; true when it has none. */
export function validateSection(form: WizardForm, section: WizardSection): boolean {
  return sectionFields(section, form.getValues())
    .map((path) => form.validateField(path).hasError)
    .every((hasError) => !hasError);
}

/** The closed Settings section's summary: values as typed, secrets only as set (docs/11 §10 rule 7). */
export function settingsSummary(values: WizardValues): string {
  return [
    ...Object.entries(values.settings).map(([name, value]) => `${name}: ${value}`),
    ...Object.entries(values.secrets)
      .filter(([, secret]) => secret !== '')
      .map(([name]) => `${name}: ••••`),
  ].join(' · ');
}
