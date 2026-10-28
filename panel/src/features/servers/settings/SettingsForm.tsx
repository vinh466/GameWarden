import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { flushSync } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button, Group, Text } from '@mantine/core';
import { schemaResolver, useForm, type FormErrors } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import {
  updateInstanceSettings,
  type AdapterSetting,
  type AdapterSettingGroup,
  type AdapterSummary,
  type SettingsView,
} from '@/api/generated';
import { getInstanceSettingsQueryKey } from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';
import { FormFooter, FormSections } from '@/components/FormSections';
import { initialSettingValues, settingValuesSchema } from './settingSchema';
import { settingPath } from './settingText';
import { groupSection, useSettingSections } from './useSettingSections';

const otherGroup = 'other';

/** A key set with the CLI that the adapter does not describe: still editable, as plain text (04 §4). */
const plain = (key: string, type: 'string' | 'secret'): AdapterSetting => ({
  key,
  type,
  group: otherGroup,
  label: null,
  help: null,
  warning: null,
  default: null,
  required: false,
  wizard: false,
  min: null,
  max: null,
  minLength: null,
  maxLength: null,
  pattern: null,
  options: [],
  restartRequired: true,
});

/**
 * The Settings tab (docs/11 §4): the adapter's settings groups as sections (D10), the advanced ones closed, with
 * Save in a footer that stays in view and counts what is unsaved. Only changed values are sent, so the others keep
 * following the adapter's defaults. A save with errors opens their sections and focuses the first one.
 */
export function SettingsForm({
  id,
  view,
  adapter,
}: {
  id: string;
  view: SettingsView;
  adapter: AdapterSummary;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const known = new Set(adapter.settings.map((setting) => setting.key));
  const extras = [
    ...Object.keys(view.settings)
      .filter((key) => !known.has(key))
      .map((key) => plain(key, 'string')),
    ...Object.keys(view.secrets)
      .filter((key) => !known.has(key))
      .map((key) => plain(key, 'secret')),
  ];
  const fields = [...adapter.settings, ...extras];
  const groups: AdapterSettingGroup[] = [
    ...adapter.settingGroups,
    ...(extras.length > 0
      ? [
          {
            id: otherGroup,
            label: { en: t('settings.other') },
            help: { en: t('settings.otherHint') },
            advanced: false,
          },
        ]
      : []),
  ];
  const [open, setOpen] = useState(() =>
    groups.filter((group) => !group.advanced).map((group) => groupSection(group.id))
  );
  const form = useForm({
    mode: 'controlled',
    initialValues: initialSettingValues(fields, view.settings),
    validate: schemaResolver(z.object(settingValuesSchema(fields, t, true)), { sync: true }),
  });
  const changed =
    Object.keys(form.values.settings).filter((key) => form.isDirty(`settings.${key}`)).length +
    Object.values(form.values.secrets).filter((secret) => secret !== '').length;

  const save = useMutation({
    mutationFn: (values: typeof form.values) =>
      updateInstanceSettings({
        path: { id },
        // Secrets stay write-only: an empty field means "unchanged".
        body: {
          settings: Object.fromEntries(
            Object.entries(values.settings).filter(([key]) => form.isDirty(`settings.${key}`))
          ),
          secrets: Object.fromEntries(
            Object.entries(values.secrets).filter(([, secret]) => secret !== '')
          ),
        },
        throwOnError: true,
      }),
    onSuccess: ({ data }, values) => {
      queryClient.setQueryData(getInstanceSettingsQueryKey({ path: { id } }), data);
      // The saved values are the new starting point for Discard and the unsaved count.
      const saved = { ...values, secrets: initialSettingValues(fields).secrets };
      form.setInitialValues(saved);
      form.setValues(saved);
      form.resetDirty(saved);
      notifications.show({ color: 'violet', message: t('settings.saved') });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const sections = useSettingSections({
    groups,
    fields,
    values: form.values,
    errors: form.errors,
    input: (path) => form.getInputProps(path),
    secretsSet: Object.fromEntries(
      Object.entries(view.secrets).map(([key, state]) => [key, state.set])
    ),
  });

  const showErrors = (errors: FormErrors) => {
    const paths = Object.keys(errors);
    const failing = fields
      .filter((setting) => paths.includes(settingPath(setting)))
      .map((setting) => groupSection(setting.group));
    flushSync(() => setOpen((current) => [...new Set([...current, ...failing])]));
    const first = paths[0];
    if (first) {
      form.getInputNode(first)?.focus();
    }
  };

  if (fields.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t('settings.none')}
      </Text>
    );
  }

  return (
    <form onSubmit={form.onSubmit((values) => save.mutate(values), showErrors)}>
      <FormSections sections={sections} open={open} onOpenChange={setOpen} />
      <FormFooter>
        <Text size="sm" c="dimmed">
          {changed > 0 ? t('sections.unsaved', { count: changed }) : t('sections.noChanges')}
        </Text>
        <Group gap="sm">
          <Button variant="default" disabled={changed === 0} onClick={() => form.reset()}>
            {t('sections.discard')}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={changed === 0}>
            {t('actions.save')}
          </Button>
        </Group>
      </FormFooter>
    </form>
  );
}
