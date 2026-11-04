import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Card, Select } from '@mantine/core';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';
import { JobsTable } from './JobsTable';

/** Every job on the machine, newest first; the server filter lives in the URL so the view can be shared. */
export function JobsScreen({
  server,
  onServerChange,
}: {
  server?: string;
  onServerChange: (server?: string) => void;
}) {
  const { t } = useTranslation();
  const instances = useQuery(queries.instances());
  const jobs = useQuery(queries.jobs(server));

  return (
    <SystemTab
      description={t('jobs.description')}
      actions={
        <Select
          placeholder={t('jobs.allServers')}
          data={(instances.data ?? []).map((instance) => ({
            value: instance.id,
            label: instance.displayName,
          }))}
          value={server ?? null}
          onChange={(value) => onServerChange(value ?? undefined)}
          clearable
          w={220}
          aria-label={t('jobs.server')}
        />
      }
    >
      {jobs.isError ? (
        <ProblemAlert error={jobs.error} onRetry={() => void jobs.refetch()} />
      ) : (
        <Card p={0}>
          <JobsTable jobs={jobs.data ?? []} loading={!jobs.data && jobs.isFetching} />
        </Card>
      )}
    </SystemTab>
  );
}
