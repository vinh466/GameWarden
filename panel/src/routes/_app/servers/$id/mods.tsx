import { IconPuzzle } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Skeleton, Stack } from '@mantine/core';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';
import { ActiveSetCard } from '@/features/servers/mods/ActiveSetCard';
import { ConfigFiles } from '@/features/servers/mods/ConfigFiles';
import { LoaderLog } from '@/features/servers/mods/LoaderLog';
import { ModsOnCard } from '@/features/servers/mods/ModsOnCard';
import { PendingSet } from '@/features/servers/mods/PendingSet';

export const Route = createFileRoute('/_app/servers/$id/mods')({
  // A game without mods answers 409; the page then says so instead of failing.
  loader: ({ context: { queryClient }, params }) =>
    Promise.all([
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
      queryClient.query({ ...queries.mods(params.id), staleTime: 'static' }).catch(() => null),
    ]),
  component: Mods,
});

/** The Mods tab (docs/13 §4.10, docs/11 §4), shown for games whose adapter has a mod loader. */
function Mods() {
  const { t } = useTranslation();
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const adapters = useQuery(queries.adapters());
  const adapter = adapters.data?.adapters.find(
    (candidate) => candidate.id === server.data?.adapterId
  );
  const mods = useQuery({ ...queries.mods(id), enabled: !!adapter?.modLoader });
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
    <Stack gap="lg">
      <ModsOnCard server={server.data} mods={mods.data} />
      {mods.data.pending && <PendingSet set={mods.data.pending} />}
      <ActiveSetCard id={id} server={server.data.displayName} mods={mods.data} />
      <ConfigFiles server={server.data} />
      <LoaderLog id={id} />
    </Stack>
  );
}
