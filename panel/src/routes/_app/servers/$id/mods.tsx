import { IconAdjustments, IconFileText, IconPuzzle } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Outlet, useLocation, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Skeleton, Stack, Tabs } from '@mantine/core';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';

export const Route = createFileRoute('/_app/servers/$id/mods')({
  // A game without mods answers 409; the page then says so instead of failing.
  loader: ({ context: { queryClient }, params }) =>
    Promise.all([
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
      queryClient.query({ ...queries.mods(params.id), staleTime: 'static' }).catch(() => null),
    ]),
  component: ModsLayout,
});

/**
 * The Mods section (docs/13 §4.10, docs/11 §4): three screens, because they are three jobs. *Mods* is the set and its
 * toolbar; *Config files* is the editor; *BepInEx log* is the loader's own log. Each is its own route, so a link can
 * point at it.
 */
function ModsLayout() {
  const { t } = useTranslation();
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const adapters = useQuery(queries.adapters());
  const mods = useQuery({ ...queries.mods(id), enabled: !!server.data });
  const adapter = adapters.data?.adapters.find(
    (candidate) => candidate.id === server.data?.adapterId
  );
  const pathname = useLocation({ select: (location) => location.pathname });
  const navigate = useNavigate();
  const current = subTabOfPath(pathname);

  if (!server.data || !adapter) {
    return <Skeleton h={240} />;
  }

  if (!adapter.modLoader) {
    return <EmptyState icon={IconPuzzle} title={t('mods.noSupport')} />;
  }

  if (!mods.data) {
    return mods.isError ? (
      <ProblemAlert error={mods.error} onRetry={() => void mods.refetch()} />
    ) : (
      <Skeleton h={240} />
    );
  }

  return (
    <Stack gap="md">
      <Tabs
        value={current}
        variant="pills"
        radius="xl"
        onChange={(value) => {
          const tab = value ?? 'mods';
          void navigate(
            tab === 'mods'
              ? { to: '/servers/$id/mods', params: { id } }
              : { to: `/servers/$id/mods/${tab}`, params: { id } }
          );
        }}
      >
        <Tabs.List>
          <Tabs.Tab value="mods" leftSection={<IconPuzzle size={16} />}>
            {t('mods.tabs.mods')}
          </Tabs.Tab>
          <Tabs.Tab value="config" leftSection={<IconAdjustments size={16} />}>
            {t('mods.tabs.config')}
          </Tabs.Tab>
          <Tabs.Tab value="log" leftSection={<IconFileText size={16} />}>
            {t('mods.tabs.log')}
          </Tabs.Tab>
        </Tabs.List>
      </Tabs>
      <Outlet />
    </Stack>
  );
}

function subTabOfPath(pathname: string): 'mods' | 'config' | 'log' {
  if (pathname.endsWith('/config')) {
    return 'config';
  }

  return pathname.endsWith('/log') ? 'log' : 'mods';
}
