import { useQuery } from '@tanstack/react-query';
import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Card, Select } from '@mantine/core';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';
import { JobsTable } from '@/features/jobs/JobsTable';

// The server filter lives in the URL, so a filtered list can be bookmarked or shared.
export const Route = createFileRoute('/_app/jobs')({
  validateSearch: z.object({ server: z.string().optional() }),
  loader: ({ context: { queryClient } }) =>
    queryClient.query({ ...queries.jobs(), staleTime: 'static' }),
  component: Jobs,
});

function Jobs() {
  const { t } = useTranslation();
  const { server } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const instances = useQuery(queries.instances());
  const jobs = useQuery(queries.jobs(server));

  return (
    <>
      <PageHeader
        title={t('jobs.title')}
        actions={
          <Select
            placeholder={t('jobs.allServers')}
            data={(instances.data ?? []).map((instance) => ({
              value: instance.id,
              label: instance.displayName,
            }))}
            value={server ?? null}
            onChange={(value) => void navigate({ search: { server: value ?? undefined } })}
            clearable
            w={220}
            aria-label={t('jobs.server')}
          />
        }
      />
      <Card p={0}>
        <JobsTable jobs={jobs.data ?? []} loading={!jobs.data && jobs.isFetching} />
      </Card>
    </>
  );
}
