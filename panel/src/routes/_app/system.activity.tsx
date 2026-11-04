import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { z } from 'zod';
import { queries } from '@/api/queries';
import { activityGroups } from '@/features/activity/activityKinds';
import { ActivityScreen } from '@/features/activity/ActivityScreen';

export const Route = createFileRoute('/_app/system/activity')({
  validateSearch: z.object({
    server: z.string().optional(),
    type: z.enum(activityGroups).optional(),
  }),
  loaderDeps: ({ search }) => ({ server: search.server }),
  loader: ({ context: { queryClient }, deps }) =>
    Promise.all([
      queryClient.query({ ...queries.activity(deps.server), staleTime: 'static' }),
      queryClient.query({ ...queries.instances(), staleTime: 'static' }),
    ]),
  component: ActivityRoute,
});

function ActivityRoute() {
  const { server, type = 'all' } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  return (
    <ActivityScreen
      server={server}
      type={type}
      onServerChange={(next) => void navigate({ search: (prev) => ({ ...prev, server: next }) })}
      onTypeChange={(next) =>
        void navigate({
          search: (prev) => ({ ...prev, type: next === 'all' ? undefined : next }),
        })
      }
    />
  );
}
