import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { Card } from '@mantine/core';
import { queries } from '@/api/queries';
import { JobsTable } from '@/features/jobs/JobsTable';

export const Route = createFileRoute('/_app/servers/$id/jobs')({
  loader: ({ context: { queryClient }, params }) =>
    queryClient.query({ ...queries.jobs(params.id), staleTime: 'static' }),
  component: ServerJobs,
});

function ServerJobs() {
  const { id } = Route.useParams();
  const jobs = useQuery(queries.jobs(id));
  return (
    <Card p={0}>
      <JobsTable
        jobs={jobs.data ?? []}
        loading={!jobs.data && jobs.isFetching}
        showServer={false}
      />
    </Card>
  );
}
