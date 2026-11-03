import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { Skeleton, Stack } from '@mantine/core';
import { queries } from '@/api/queries';
import { ActiveSetCard } from '@/features/servers/mods/ActiveSetCard';
import { ModsHeader } from '@/features/servers/mods/ModsHeader';
import { PendingSet } from '@/features/servers/mods/PendingSet';

export const Route = createFileRoute('/_app/servers/$id/mods/')({
  component: ModsIndex,
});

/** The set itself: its status, the set that is building or failed, then the searchable list. */
function ModsIndex() {
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const mods = useQuery(queries.mods(id));

  if (!server.data || !mods.data) {
    return <Skeleton h={240} />;
  }

  return (
    <Stack gap="lg">
      <ModsHeader server={server.data} mods={mods.data} />
      {mods.data.pending && <PendingSet set={mods.data.pending} />}
      <ActiveSetCard id={id} server={server.data.displayName} mods={mods.data} />
    </Stack>
  );
}
