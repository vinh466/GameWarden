import { IconApi } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Skeleton, Stack, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';
import { IntegrationActions } from '@/features/servers/integration/IntegrationActions';
import { IntegrationPlayers } from '@/features/servers/integration/IntegrationPlayers';
import { IntegrationSummary } from '@/features/servers/integration/IntegrationSummary';
import { localized } from '@/features/servers/settings/settingText';
import { useServerCommands } from '@/features/servers/useServerCommands';

export const Route = createFileRoute('/_app/servers/$id/integration')({
  // An adapter without an integration answers 409; the page says so instead of failing.
  loader: ({ context: { queryClient }, params }) =>
    Promise.all([
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
      queryClient
        .query({ ...queries.integration(params.id), staleTime: 'static' })
        .catch(() => null),
    ]),
  component: Integration,
});

/**
 * The game API tab (docs/16): the game's view of itself and its players while it runs, and the actions its provider
 * exposes. The tab appears only for an adapter with an integration; the UI is provider-neutral.
 */
function Integration() {
  const { t, i18n } = useTranslation();
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const adapters = useQuery(queries.adapters());
  const adapter = adapters.data?.adapters.find(
    (candidate) => candidate.id === server.data?.adapterId
  );
  const integration = adapter?.integration;
  const view = useQuery({ ...queries.integration(id), enabled: !!integration });
  const { run, pending } = useServerCommands();

  if (!server.data || !adapter) {
    return <Skeleton h={240} />;
  }

  if (!integration) {
    return <EmptyState icon={IconApi} title={t('integration.noSupport')} />;
  }

  if (!view.data) {
    return view.isError ? (
      <ProblemAlert error={view.error} onRetry={() => void view.refetch()} />
    ) : (
      <Skeleton h={240} />
    );
  }

  if (!view.data.online) {
    return (
      <Alert
        color="gray"
        icon={<IconApi aria-hidden />}
        title={localized(integration.label, i18n.resolvedLanguage) ?? t('server.tabs.integration')}
      >
        <Stack gap="sm" align="flex-start">
          <Text size="sm">{t('integration.offline')}</Text>
          <Button
            size="xs"
            variant="light"
            loading={pending(server.data) === 'start'}
            onClick={() => run(server.data, 'start')}
          >
            {t('actions.start')}
          </Button>
        </Stack>
      </Alert>
    );
  }

  return (
    <Stack gap="lg">
      {view.data.capabilities.includes('overview') && (
        <IntegrationSummary
          serverName={server.data.displayName}
          info={view.data.info}
          metrics={view.data.metrics}
        />
      )}
      <IntegrationActions id={id} server={server.data.displayName} view={view.data} />
      <IntegrationPlayers id={id} server={server.data.displayName} view={view.data} />
    </Stack>
  );
}
