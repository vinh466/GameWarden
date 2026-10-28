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

/** One schema for the whole wizard; each step validates only its own fields (`stepFields`). */
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

/** The form paths a step owns: 1 = name, 2 = settings and secrets. */
export function stepFields(step: number, values: WizardValues): string[] {
  if (step === 1) {
    return ['displayName', 'id'];
  }

  if (step === 2) {
    return [
      ...Object.keys(values.settings).map((name) => `settings.${name}`),
      ...Object.keys(values.secrets).map((name) => `secrets.${name}`),
    ];
  }

  return [];
}
