import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { z } from 'zod';
import { queries } from '@/api/queries';
import { JobsScreen } from '@/features/jobs/JobsScreen';

export const Route = createFileRoute('/_app/system/jobs')({
  validateSearch: z.object({ server: z.string().optional() }),
  loaderDeps: ({ search }) => ({ server: search.server }),
  loader: ({ context: { queryClient }, deps }) =>
    Promise.all([
      queryClient.query({ ...queries.jobs(deps.server), staleTime: 'static' }),
      queryClient.query({ ...queries.instances(), staleTime: 'static' }),
    ]),
  component: JobsRoute,
});

function JobsRoute() {
  const { server } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  return (
    <JobsScreen
      server={server}
      onServerChange={(next) => void navigate({ search: { server: next } })}
    />
  );
}
