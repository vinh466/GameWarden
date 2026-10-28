import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { flushSync } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Badge, Button, Group, PasswordInput, Stack, Text, TextInput } from '@mantine/core';
import { schemaResolver, useForm, type FormErrors } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { updateInstanceSettings, type SettingsView } from '@/api/generated';
import { getInstanceSettingsQueryKey } from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';
import { FormFooter, FormSections } from '@/components/FormSections';

// Same limits as Core's SettingsValidation: single launch arguments, no control characters.
const value = z
  .string()
  .max(1024)
  .regex(/^[^\p{Cc}]*$/u);

const groups = ['settings', 'secrets'] as const;

/**
 * The Settings tab (docs/11 §4): the same sections as the new server wizard, all open and free to fold, with Save in
 * a footer that stays in view and says what is unsaved. A save with errors opens their sections and focuses the
 * first one (Mantine form's "focus first input with error").
 */
export function SettingsForm({ id, view }: { id: string; view: SettingsView }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState<string[]>([...groups]);
  const names = [...new Set([...view.requiredSettings, ...Object.keys(view.settings)])];
  const secretNames = Object.keys(view.secrets);
  const emptySecrets = Object.fromEntries(secretNames.map((name) => [name, '']));

  const schema = z.object({
    settings: z.object(
      Object.fromEntries(
        names.map((name) => [
          name,
          view.requiredSettings.includes(name) ? value.min(1, t('settings.required')) : value,
        ])
      )
    ),
    secrets: z.object(Object.fromEntries(secretNames.map((name) => [name, value]))),
  });
  const form = useForm({
    mode: 'controlled',
    initialValues: {
      settings: Object.fromEntries(names.map((name) => [name, view.settings[name] ?? ''])),
      secrets: emptySecrets,
    },
    validate: schemaResolver(schema, { sync: true }),
  });
  const changed =
    names.filter((name) => form.isDirty(`settings.${name}`)).length +
    secretNames.filter((name) => form.values.secrets[name] !== '').length;
  const invalid = (group: string) =>
    Object.keys(form.errors).some((path) => path.startsWith(`${group}.`));

  const save = useMutation({
    mutationFn: (values: typeof form.values) =>
      updateInstanceSettings({
        path: { id },
        // Secrets stay write-only: an empty field means "unchanged".
        body: {
          settings: values.settings,
          secrets: Object.fromEntries(
            Object.entries(values.secrets).filter(([, secret]) => secret !== '')
          ),
        },
        throwOnError: true,
      }),
    onSuccess: ({ data }, values) => {
      queryClient.setQueryData(getInstanceSettingsQueryKey({ path: { id } }), data);
      // The saved values are the new starting point for Discard and the unsaved count.
      const saved = { settings: values.settings, secrets: emptySecrets };
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

  const showErrors = (errors: FormErrors) => {
    const paths = Object.keys(errors);
    flushSync(() =>
      setOpen((current) => [
        ...new Set([
          ...current,
          ...groups.filter((group) => paths.some((path) => path.startsWith(`${group}.`))),
        ]),
      ])
    );
    const first = paths[0];
    if (first) {
      form.getInputNode(first)?.focus();
    }
  };

  const sections = [
    {
      id: 'settings',
      title: t('settings.title'),
      summary: names.map((name) => `${name}: ${form.values.settings[name] ?? ''}`).join(' · '),
      invalid: invalid('settings'),
      children: (
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            {t('settings.settingsHint')}
          </Text>
          {names.length === 0 && (
            <Text size="sm" c="dimmed">
              {t('settings.none')}
            </Text>
          )}
          {names.map((name) => (
            <TextInput
              key={form.key(`settings.${name}`)}
              label={name}
              withAsterisk={view.requiredSettings.includes(name)}
              {...form.getInputProps(`settings.${name}`)}
            />
          ))}
        </Stack>
      ),
    },
    ...(secretNames.length > 0
      ? [
          {
            id: 'secrets',
            title: t('settings.secrets'),
            summary: secretNames
              .map(
                (name) =>
                  `${name}: ${view.secrets[name]?.set ? t('settings.secretSet') : t('settings.secretNotSet')}`
              )
              .join(' · '),
            invalid: invalid('secrets'),
            children: (
              <Stack gap="sm">
                <Text size="sm" c="dimmed">
                  {t('settings.secretsHint')}
                </Text>
                {secretNames.map((name) => (
                  <PasswordInput
                    key={form.key(`secrets.${name}`)}
                    label={
                      <Group gap={6} component="span">
                        {name}
                        <Badge
                          size="xs"
                          color={view.secrets[name]?.set ? 'green' : 'red'}
                          component="span"
                        >
                          {view.secrets[name]?.set
                            ? t('settings.secretSet')
                            : t('settings.secretNotSet')}
                        </Badge>
                      </Group>
                    }
                    placeholder={t('settings.secretPlaceholder')}
                    autoComplete="new-password"
                    {...form.getInputProps(`secrets.${name}`)}
                  />
                ))}
              </Stack>
            ),
          },
        ]
      : []),
  ];

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
