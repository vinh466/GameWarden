import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Group, Stepper, Text, useMatches } from '@mantine/core';
import type { AdapterSummary } from '@/api/generated';
import { queries } from '@/api/queries';
import { GamePicker } from './GamePicker';
import { NameFields } from './NameFields';
import { ReviewChecks } from './ReviewChecks';
import { SettingsFields } from './SettingsFields';
import { useCreateServer } from './useCreateServer';
import { stepFields, useWizardForm } from './wizardForm';
import classes from './NewServerWizard.module.css';

const steps = ['game', 'name', 'settings', 'review'] as const;

/**
 * docs/11 §4: game → name → settings → review, then create and install (docs/07 §6.3). From `xs` up the steps run
 * down the left and show what was chosen; on a phone they shrink to a row of numbers above the step.
 */
export function NewServerWizard() {
  const { t } = useTranslation();
  const vertical = useMatches({ base: false, xs: true });
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

  const chosen: (string | undefined)[] = [adapter?.name, form.values.displayName || undefined];
  const body = [
    <GamePicker
      key="game"
      adapters={adapters.data?.adapters ?? []}
      selected={adapter?.id}
      onPick={pick}
    />,
    <NameFields key="name" form={form} />,
    <SettingsFields key="settings" form={form} />,
    adapter && system.data ? (
      <ReviewChecks key="review" adapter={adapter} system={system.data} />
    ) : null,
  ][step];

  return (
    <div className={vertical ? classes.split : undefined}>
      <Stepper
        active={step}
        onStepClick={(target) => target < step && setStep(target)}
        allowNextStepsSelect={false}
        orientation={vertical ? 'vertical' : 'horizontal'}
        size="sm"
        className={vertical ? classes.rail : undefined}
        mb={vertical ? undefined : 'lg'}
      >
        {steps.map((name, index) => (
          <Stepper.Step
            key={name}
            label={vertical ? t(`wizard.steps.${name}`) : undefined}
            description={vertical ? chosen[index] : undefined}
            aria-label={t(`wizard.steps.${name}`)}
          />
        ))}
      </Stepper>

      <div className={classes.body}>
        {!vertical && (
          <Text fw={600} mb="sm">
            {t(`wizard.steps.${steps[step] ?? 'game'}`)}
          </Text>
        )}
        {body}
        {step > 0 && (
          <Group justify="space-between" mt="xl">
            <Button variant="default" onClick={() => setStep((current) => current - 1)}>
              {t('actions.back')}
            </Button>
            {step < steps.length - 1 ? (
              <Button onClick={next}>{t('actions.next')}</Button>
            ) : (
              <Button loading={create.isPending} onClick={() => create.mutate()}>
                {create.isPending ? t('wizard.creating') : t('wizard.create')}
              </Button>
            )}
          </Group>
        )}
      </div>
    </div>
  );
}
