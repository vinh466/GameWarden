import { useEffect, useRef } from 'react';
import {
  IconApi,
  IconFileInfo,
  IconLayoutDashboard,
  IconListCheck,
  IconPuzzle,
  IconSettings,
  IconTerminal2,
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Outlet, useLocation, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Badge, Group, ScrollArea, Skeleton, Tabs, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';
import { ProblemAlert } from '@/components/ProblemAlert';
import { BusyBadge, StatusBadge } from '@/components/StatusBadge';
import { ServerActions } from '@/features/servers/ServerActions';
import { serverTabs, tabOfPath, type ServerTab } from '@/features/servers/serverTabs';
import { localized } from '@/features/servers/settings/settingText';

const icons = {
  overview: IconLayoutDashboard,
  console: IconTerminal2,
  settings: IconSettings,
  mods: IconPuzzle,
  integration: IconApi,
  jobs: IconListCheck,
  info: IconFileInfo,
};

export const Route = createFileRoute('/_app/servers/$id')({
  loader: ({ context: { queryClient }, params }) =>
    Promise.all([
      queryClient.query({ ...queries.instance(params.id), staleTime: 'static' }),
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
    ]),
  component: ServerLayout,
  errorComponent: ({ error }) => <ProblemAlert error={error} />,
});

/** docs/11 §3: header with state and actions, then tabs; each tab is its own route, so it has a link. */
function ServerLayout() {
  const { t, i18n } = useTranslation();
  const { id } = Route.useParams();
  const navigate = useNavigate();
  const pathname = useLocation({ select: (location) => location.pathname });
  const server = useQuery(queries.instance(id));
  const adapters = useQuery(queries.adapters());
  const adapter = adapters.data?.adapters.find(
    (candidate) => candidate.id === server.data?.adapterId
  );
  const integration = adapter?.integration;
  const integrationLabel = localized(integration?.label, i18n.resolvedLanguage);
  const current = tabOfPath(pathname);
  const listRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    listRef.current
      ?.querySelector('[data-active]')
      ?.scrollIntoView({ inline: 'center', block: 'nearest' });
  }, [current]);

  if (!server.data) {
    return server.isError ? (
      <ProblemAlert error={server.error} onRetry={() => void server.refetch()} />
    ) : (
      <Skeleton h={120} />
    );
  }

  const data = server.data;
  return (
    <>
      <PageHeader
        crumbs={[{ label: t('nav.servers'), to: '/servers' }, { label: data.id }]}
        title={data.displayName}
        status={
          <Group gap={6} wrap="nowrap">
            <StatusBadge state={data.state} size="lg" />
            {data.activeJobId && (
              <BusyBadge label={t('feedback.busyReason', { job: data.activeJobId })} />
            )}
            <Badge variant="outline" color="gray" visibleFrom="sm">
              {t('desired.label')}: {t(`desired.${data.desiredState}`)}
            </Badge>
          </Group>
        }
        actions={<ServerActions server={data} />}
      />
      <Tabs
        value={current}
        onChange={(value) => {
          const tab = (value ?? 'overview') as ServerTab;
          void navigate(
            tab === 'overview'
              ? { to: '/servers/$id', params: { id } }
              : { to: `/servers/$id/${tab}`, params: { id } }
          );
        }}
        mb="lg"
      >
        <ScrollArea type="auto" scrollbarSize={6}>
          <Tabs.List ref={listRef} style={{ flexWrap: 'nowrap' }}>
            {serverTabs(adapter?.modLoader, !!integration).map((tab) => {
              const Icon = icons[tab];
              const label =
                tab === 'integration'
                  ? (integrationLabel ?? t('server.tabs.integration'))
                  : t(`server.tabs.${tab}`);
              return (
                <Tabs.Tab key={tab} value={tab} leftSection={<Icon size={16} />}>
                  <Text size="sm" inherit>
                    {label}
                  </Text>
                </Tabs.Tab>
              );
            })}
          </Tabs.List>
        </ScrollArea>
      </Tabs>
      <Outlet />
    </>
  );
}
