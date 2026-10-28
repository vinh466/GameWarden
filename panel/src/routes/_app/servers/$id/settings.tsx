import { IconInfoCircle } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  PasswordInput,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { schemaResolver, useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { updateInstanceSettings, type SettingsView } from '@/api/generated';
import { getInstanceSettingsQueryKey } from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { useServerCommands } from '@/features/servers/useServerCommands';

export const Route = createFileRoute('/_app/servers/$id/settings')({
  loader: ({ context: { queryClient }, params }) =>
    queryClient.query({ ...queries.settings(params.id), staleTime: 'static' }),
  component: Settings,
});

function Settings() {
  const { id } = Route.useParams();
  const settings = useQuery(queries.settings(id));
  return settings.data ? <SettingsForm id={id} view={settings.data} /> : <Skeleton h={240} />;
}

// Same limits as Core's SettingsValidation: single launch arguments, no control characters.
const value = z
  .string()
  .max(1024)
  .regex(/^[^\p{Cc}]*$/u);

function SettingsForm({ id, view }: { id: string; view: SettingsView }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const server = useQuery(queries.instance(id));
  const { run } = useServerCommands();
  const names = [...new Set([...view.requiredSettings, ...Object.keys(view.settings)])];
  const secretNames = Object.keys(view.secrets);

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
    mode: 'uncontrolled',
    initialValues: {
      settings: Object.fromEntries(names.map((name) => [name, view.settings[name] ?? ''])),
      secrets: Object.fromEntries(secretNames.map((name) => [name, ''])),
    },
    validate: schemaResolver(schema, { sync: true }),
  });

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
    onSuccess: ({ data }) => {
      queryClient.setQueryData(getInstanceSettingsQueryKey({ path: { id } }), data);
      form.setFieldValue('secrets', Object.fromEntries(secretNames.map((name) => [name, ''])));
      form.resetDirty();
      notifications.show({ color: 'violet', message: t('settings.saved') });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  return (
    <form onSubmit={form.onSubmit((values) => save.mutate(values))}>
      <Stack gap="lg" maw={720}>
        {view.restartRequired && server.data && (
          <Alert color="blue" icon={<IconInfoCircle aria-hidden />}>
            <Group justify="space-between" gap="xs">
              <Text size="sm">{t('settings.restartRequired')}</Text>
              <Button
                size="xs"
                variant="light"
                onClick={() => server.data && run(server.data, 'restart')}
              >
                {t('settings.restartNow')}
              </Button>
            </Group>
          </Alert>
        )}
        <Card>
          <Title order={5}>{t('settings.title')}</Title>
          <Text size="sm" c="dimmed" mb="md">
            {t('settings.settingsHint')}
          </Text>
          <Stack gap="sm">
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
        </Card>
        {secretNames.length > 0 && (
          <Card>
            <Title order={5}>{t('settings.secrets')}</Title>
            <Text size="sm" c="dimmed" mb="md">
              {t('settings.secretsHint')}
            </Text>
            <Stack gap="sm">
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
          </Card>
        )}
        <Group justify="flex-end">
          <Button type="submit" loading={save.isPending}>
            {t('actions.save')}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
