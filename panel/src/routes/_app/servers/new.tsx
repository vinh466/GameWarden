import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Button, Card, Group, Stepper } from '@mantine/core';
import type { AdapterSummary } from '@/api/generated';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';
import { GamePicker } from '@/features/servers/wizard/GamePicker';
import { NameFields } from '@/features/servers/wizard/NameFields';
import { ReviewChecks } from '@/features/servers/wizard/ReviewChecks';
import { SettingsFields } from '@/features/servers/wizard/SettingsFields';
import { useCreateServer } from '@/features/servers/wizard/useCreateServer';
import { stepFields, useWizardForm } from '@/features/servers/wizard/wizardForm';

export const Route = createFileRoute('/_app/servers/new')({
  loader: ({ context: { queryClient } }) =>
    Promise.all([
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
      queryClient.query({ ...queries.system(), staleTime: 'static' }),
    ]),
  component: NewServer,
});

/** docs/11 §4: game → name → settings → review, then create and install (docs/07 §6.3). */
function NewServer() {
  const { t } = useTranslation();
  const adapters = useQuery(queries.adapters());
  const system = useQuery(queries.system());
  const [step, setStep] = useState(0);
  const [adapter, setAdapter] = useState<AdapterSummary | null>(null);
  const form = useWizardForm();
  const create = useCreateServer(form, adapter);

  const pick = (choice: AdapterSummary) => {
    setAdapter(choice);
    form.setFieldValue(
      'settings',
      Object.fromEntries(choice.requiredSettings.map((name) => [name, '']))
    );
    form.setFieldValue(
      'secrets',
      Object.fromEntries(choice.requiredSecrets.map((name) => [name, '']))
    );
    setStep(1);
  };

  const next = () => {
    const invalid = stepFields(step, form.values).some(
      (field) => form.validateField(field).hasError
    );
    if (!invalid) {
      setStep((current) => current + 1);
    }
  };

  return (
    <>
      <PageHeader
        crumbs={[{ label: t('nav.servers'), to: '/servers' }, { label: t('wizard.title') }]}
        title={t('wizard.title')}
      />
      <Card maw={860}>
        <Stepper
          active={step}
          onStepClick={(target) => target < step && setStep(target)}
          size="sm"
          allowNextStepsSelect={false}
        >
          <Stepper.Step label={t('wizard.steps.game')}>
            <GamePicker
              adapters={adapters.data?.adapters ?? []}
              selected={adapter?.id}
              onPick={pick}
            />
          </Stepper.Step>
          <Stepper.Step label={t('wizard.steps.name')}>
            <NameFields form={form} />
          </Stepper.Step>
          <Stepper.Step label={t('wizard.steps.settings')}>
            <SettingsFields form={form} />
          </Stepper.Step>
          <Stepper.Step label={t('wizard.steps.review')}>
            {adapter && system.data && <ReviewChecks adapter={adapter} system={system.data} />}
          </Stepper.Step>
        </Stepper>

        {step > 0 && (
          <Group justify="space-between" mt="xl">
            <Button variant="default" onClick={() => setStep((current) => current - 1)}>
              {t('actions.back')}
            </Button>
            {step < 3 ? (
              <Button onClick={next}>{t('actions.next')}</Button>
            ) : (
              <Button loading={create.isPending} onClick={() => create.mutate()}>
                {create.isPending ? t('wizard.creating') : t('wizard.create')}
              </Button>
            )}
          </Group>
        )}
      </Card>
    </>
  );
}
