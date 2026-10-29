import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Drawer, Group, Skeleton, Text } from '@mantine/core';
import type { AdapterSummary } from '@/api/generated';
import { queries } from '@/api/queries';
import { FormFooter, FormSections, type FormSection } from '@/components/FormSections';
import { useSectionFlow } from '@/components/sectionFlow';
import { initialSettingValues } from '../settings/settingSchema';
import { useSettingSections } from '../settings/useSettingSections';
import { GamePicker } from './GamePicker';
import { NameFields } from './NameFields';
import { ReviewChecks } from './ReviewChecks';
import { useCreateServer } from './useCreateServer';
import {
  sectionFields,
  settingsPlaceholder,
  useWizardForm,
  validateSection,
  wizardFields,
  wizardSectionIds,
} from './wizardForm';

/**
 * docs/11 §4: game → name → the game's settings groups → review, as sections of one scrolling drawer, then create
 * and install (docs/07 §6.3). Next checks the open section before the next one opens; Back and Next stay at the
 * bottom. The wizard asks only what the game needs to start; the rest keeps its default (Settings tab).
 */
export function NewServerWizard() {
  const { t } = useTranslation();
  const adapters = useQuery(queries.adapters());
  const system = useQuery(queries.system());
  const [adapter, setAdapter] = useState<AdapterSummary | null>(null);
  const [gameMissing, setGameMissing] = useState(false);
  const fields = wizardFields(adapter);
  const form = useWizardForm(fields);
  const create = useCreateServer(form, adapter);
  const ids = wizardSectionIds(adapter);
  const flow = useSectionFlow(ids.length);
  const current = ids[flow.active] ?? 'game';
  const hasErrors = (section: string) =>
    sectionFields(section, adapter).some((path) => !!form.errors[path]);
  const settingSections = useSettingSections({
    adapterId: adapter?.id,
    groups: adapter?.settingGroups ?? [],
    fields,
    values: form.values,
    errors: form.errors,
    input: (path) => form.getInputProps(path),
  });

  const pick = (choice: AdapterSummary) => {
    if (choice.id !== adapter?.id) {
      const initial = initialSettingValues(wizardFields(choice));
      form.setFieldValue('settings', initial.settings);
      form.setFieldValue('secrets', initial.secrets);
      // Another game has other settings: they are filled in again before the review. Its first settings section
      // follows Game and Name.
      flow.rewind(2);
    }

    setAdapter(choice);
    setGameMissing(false);
    flow.next();
  };

  const next = () => {
    if (current === 'game' && !adapter) {
      setGameMissing(true);
    } else if (validateSection(form, sectionFields(current, adapter))) {
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
    ...(adapter
      ? settingSections
      : [{ id: settingsPlaceholder, title: t('wizard.steps.settings'), children: null }]),
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
            {t('sections.step', { index: flow.active + 1, count: ids.length })}
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
