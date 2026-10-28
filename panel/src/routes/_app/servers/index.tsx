import { IconPlus, IconServer2 } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Card, Group, Stack, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { AnchorLink, ButtonLink } from '@/components/links';
import { PageHeader } from '@/components/PageHeader';
import { ProblemAlert } from '@/components/ProblemAlert';
import { BusyBadge, StatusBadge } from '@/components/StatusBadge';
import { portsLabel } from '@/features/servers/ports';
import { ServerActions } from '@/features/servers/ServerActions';
import { NewServerDrawer } from '@/features/servers/wizard/NewServerDrawer';

// `?new=true` opens the new server wizard over the list, so links (dashboard, spotlight) and Back work with it.
export const Route = createFileRoute('/_app/servers/')({
  validateSearch: z.object({ new: z.boolean().optional() }),
  loader: ({ context: { queryClient } }) =>
    Promise.all([
      queryClient.query({ ...queries.instances(), staleTime: 'static' }),
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
    ]),
  component: Servers,
});

function Servers() {
  const { t } = useTranslation();
  const { new: creating } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const instances = useQuery(queries.instances());
  const adapters = useQuery(queries.adapters());
  const adapterName = (id: string) =>
    adapters.data?.adapters.find((adapter) => adapter.id === id)?.name ?? id;

  return (
    <>
      <PageHeader
        title={t('nav.servers')}
        actions={
          <ButtonLink to="/servers" search={{ new: true }} leftSection={<IconPlus size={16} />}>
            {t('nav.newServer')}
          </ButtonLink>
        }
      />
      <NewServerDrawer opened={!!creating} onClose={() => void navigate({ search: {} })} />
      {instances.isError ? (
        <ProblemAlert error={instances.error} onRetry={() => void instances.refetch()} />
      ) : instances.data?.length === 0 ? (
        <Card>
          <EmptyState
            icon={IconServer2}
            title={t('dashboard.noServers')}
            text={t('dashboard.gettingStartedText')}
            action={
              <ButtonLink
                to="/servers"
                search={{ new: true }}
                leftSection={<IconPlus size={16} />}
                mt="sm"
              >
                {t('nav.newServer')}
              </ButtonLink>
            }
          />
        </Card>
      ) : (
        <Card p={0}>
          <DataTable
            minHeight={160}
            fetching={instances.isFetching && !instances.data}
            records={instances.data ?? []}
            idAccessor="id"
            columns={[
              {
                accessor: 'displayName',
                title: t('jobs.server'),
                render: (server) => (
                  <Stack gap={0}>
                    <AnchorLink to="/servers/$id" params={{ id: server.id }} fw={600}>
                      {server.displayName}
                    </AnchorLink>
                    <Text size="xs" c="dimmed" ff="monospace">
                      {server.id}
                    </Text>
                  </Stack>
                ),
              },
              {
                accessor: 'adapterId',
                title: t('server.adapter'),
                render: (server) => adapterName(server.adapterId),
                visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
              },
              {
                accessor: 'state',
                title: t('server.status'),
                render: (server) => (
                  <Group gap={6} wrap="nowrap">
                    <StatusBadge state={server.state} />
                    {server.activeJobId && (
                      <BusyBadge label={t('feedback.busyReason', { job: server.activeJobId })} />
                    )}
                  </Group>
                ),
              },
              {
                accessor: 'ports',
                title: t('server.ports'),
                render: (server) => (
                  <Text size="sm" ff="monospace">
                    {portsLabel(server)}
                  </Text>
                ),
                visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.md})`,
              },
              {
                accessor: 'actions',
                title: '',
                textAlign: 'right',
                render: (server) => <ServerActions server={server} compact />,
              },
            ]}
          />
        </Card>
      )}
    </>
  );
}
