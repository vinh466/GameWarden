import { useQuery } from '@tanstack/react-query';
import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Card, Select, Skeleton } from '@mantine/core';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';
import { ProblemAlert } from '@/components/ProblemAlert';
import { ActivityFeed } from '@/features/activity/ActivityFeed';

// The feed of docs/11 §2 from Core's `events` table; the server filter lives in the URL, as on the Jobs page.
export const Route = createFileRoute('/_app/activity')({
  validateSearch: z.object({ server: z.string().optional() }),
  loaderDeps: ({ search }) => ({ server: search.server }),
  loader: ({ context: { queryClient }, deps }) =>
    queryClient.query({ ...queries.activity(deps.server), staleTime: 'static' }),
  component: Activity,
});

function Activity() {
  const { t } = useTranslation();
  const { server } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const instances = useQuery(queries.instances());
  const activity = useQuery(queries.activity(server));

  return (
    <>
      <PageHeader
        title={t('activity.title')}
        actions={
          <Select
            placeholder={t('activity.allServers')}
            clearable
            w={220}
            value={server ?? null}
            onChange={(value) => void navigate({ search: { server: value ?? undefined } })}
            data={(instances.data ?? []).map((instance) => ({
              value: instance.id,
              label: instance.displayName,
            }))}
            aria-label={t('jobs.server')}
          />
        }
      />
      <Card maw={820}>
        {activity.isError ? (
          <ProblemAlert error={activity.error} onRetry={() => void activity.refetch()} />
        ) : activity.data ? (
          <ActivityFeed items={activity.data} />
        ) : (
          <Skeleton h={240} />
        )}
      </Card>
    </>
  );
}
