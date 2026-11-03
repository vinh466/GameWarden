import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { Skeleton } from '@mantine/core';
import { queries } from '@/api/queries';
import { ConfigFiles } from '@/features/servers/mods/ConfigFiles';

export const Route = createFileRoute('/_app/servers/$id/mods/config')({
  component: ModsConfig,
});

/** The active set's config files on their own screen (docs/13 §4.7): list, editor and history, without the set table. */
function ModsConfig() {
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));

  return server.data ? <ConfigFiles server={server.data} /> : <Skeleton h={240} />;
}
