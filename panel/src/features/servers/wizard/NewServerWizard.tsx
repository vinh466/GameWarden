import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Drawer, Group, Skeleton, Text } from '@mantine/core';
import type { AdapterSummary } from '@/api/generated';
import { queries } from '@/api/queries';
import { FormFooter, FormSections, type FormSection } from '@/components/FormSections';
import { useSectionFlow } from '@/components/sectionFlow';
import { GamePicker } from './GamePicker';
import { NameFields } from './NameFields';
import { ReviewChecks } from './ReviewChecks';
import { SettingsFields } from './SettingsFields';
import { useCreateServer } from './useCreateServer';
import {
  sectionFields,
  settingsSummary,
  useWizardForm,
  validateSection,
  wizardSections,
} from './wizardForm';

/**
 * docs/11 §4: game → name → settings → review as sections of one scrolling drawer, then create and install
 * (docs/07 §6.3). Next checks the open section before the next one opens; Back and Next stay at the bottom.
 */
export function NewServerWizard() {
  const { t } = useTranslation();
  const adapters = useQuery(queries.adapters());
  const system = useQuery(queries.system());
  const [adapter, setAdapter] = useState<AdapterSummary | null>(null);
  const [gameMissing, setGameMissing] = useState(false);
  const form = useWizardForm();
  const create = useCreateServer(form, adapter);
  const flow = useSectionFlow(wizardSections.length);
  const current = wizardSections[flow.active] ?? 'game';
  const hasErrors = (section: 'name' | 'settings') =>
    sectionFields(section, form.values).some((path) => !!form.errors[path]);

  const pick = (choice: AdapterSummary) => {
    if (choice.id !== adapter?.id) {
      form.setFieldValue(
        'settings',
        Object.fromEntries(choice.requiredSettings.map((name) => [name, '']))
      );
      form.setFieldValue(
        'secrets',
        Object.fromEntries(choice.requiredSecrets.map((name) => [name, '']))
      );
      // Another game has other settings: they are filled in again before the review.
      flow.rewind(wizardSections.indexOf('settings'));
    }

    setAdapter(choice);
    setGameMissing(false);
    flow.next();
  };

  const next = () => {
    if (current === 'game' && !adapter) {
      setGameMissing(true);
    } else if (validateSection(form, current)) {
      flow.next();
    }
  };

  const sections: FormSection[] = [
    {
      id: 'game',
      title: t('wizard.steps.game'),
      summary: adapter && `${adapter.name} · ${t('wizard.level', { level: adapter.level })}`,
      invalid: gameMissing,
      children: (
        <GamePicker
          adapters={adapters.data?.adapters ?? []}
          selected={adapter?.id}
          missing={gameMissing}
          onPick={pick}
        />
      ),
    },
    {
      id: 'name',
      title: t('wizard.steps.name'),
      summary: form.values.displayName && `${form.values.displayName} · ${form.values.id}`,
      invalid: hasErrors('name'),
      children: <NameFields form={form} />,
    },
    {
      id: 'settings',
      title: t('wizard.steps.settings'),
      summary: settingsSummary(form.values) || t('wizard.noSettings'),
      invalid: hasErrors('settings'),
      children: <SettingsFields form={form} />,
    },
    {
      id: 'review',
      title: t('wizard.steps.review'),
      children:
        adapter && system.data ? (
          <ReviewChecks adapter={adapter} system={system.data} />
        ) : (
          <Skeleton h={120} />
        ),
    },
  ];

  return (
    <>
      <Drawer.Body flex="1 0 auto">
        <FormSections sections={sections} flow={flow} />
      </Drawer.Body>
      <FormFooter>
        <Button variant="default" disabled={flow.isFirst} onClick={flow.back}>
          {t('actions.back')}
        </Button>
        <Group gap="md">
          <Text size="sm" c="dimmed">
            {t('sections.step', { index: flow.active + 1, count: wizardSections.length })}
          </Text>
          {flow.isLast ? (
            <Button loading={create.isPending} onClick={() => create.mutate()}>
              {create.isPending ? t('wizard.creating') : t('wizard.create')}
            </Button>
          ) : (
            <Button onClick={next}>{t('actions.next')}</Button>
          )}
        </Group>
      </FormFooter>
    </>
  );
}
