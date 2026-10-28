import { IconInfoCircle } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Group, Skeleton, Stack, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { SettingsForm } from '@/features/servers/settings/SettingsForm';
import { useServerCommands } from '@/features/servers/useServerCommands';

export const Route = createFileRoute('/_app/servers/$id/settings')({
  loader: ({ context: { queryClient }, params }) =>
    Promise.all([
      queryClient.query({ ...queries.settings(params.id), staleTime: 'static' }),
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
    ]),
  component: Settings,
});

function Settings() {
  const { t } = useTranslation();
  const { id } = Route.useParams();
  const settings = useQuery(queries.settings(id));
  const server = useQuery(queries.instance(id));
  const adapters = useQuery(queries.adapters());
  const adapter = adapters.data?.adapters.find(
    (candidate) => candidate.id === server.data?.adapterId
  );
  const { run } = useServerCommands();
  if (!settings.data || !adapter) {
    return <Skeleton h={240} />;
  }

  return (
    <Stack gap="lg" maw={720}>
      {settings.data.restartRequired && server.data && (
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
      <SettingsForm id={id} view={settings.data} adapter={adapter} />
    </Stack>
  );
}
