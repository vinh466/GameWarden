import { useQuery } from '@tanstack/react-query';
import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Card, Group, SegmentedControl, Select, Skeleton } from '@mantine/core';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';
import { ProblemAlert } from '@/components/ProblemAlert';
import { ActivityFeed } from '@/features/activity/ActivityFeed';
import { activityGroup, activityGroups } from '@/features/activity/activityKinds';

// The feed of docs/11 §2 from Core's `events` table; both filters live in the URL, as on the Jobs page.
export const Route = createFileRoute('/_app/activity')({
  validateSearch: z.object({
    server: z.string().optional(),
    type: z.enum(activityGroups).optional(),
  }),
  loaderDeps: ({ search }) => ({ server: search.server }),
  loader: ({ context: { queryClient }, deps }) =>
    queryClient.query({ ...queries.activity(deps.server), staleTime: 'static' }),
  component: Activity,
});

function Activity() {
  const { t } = useTranslation();
  const { server, type = 'all' } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const instances = useQuery(queries.instances());
  const activity = useQuery(queries.activity(server));
  const items = activity.data?.filter((item) => type === 'all' || activityGroup(item) === type);

  return (
    <>
      <PageHeader
        title={t('activity.title')}
        actions={
          <Group gap="xs">
            <SegmentedControl
              size="xs"
              value={type}
              onChange={(value) =>
                void navigate({
                  search: (prev) => ({
                    ...prev,
                    type: value === 'all' ? undefined : (value as (typeof activityGroups)[number]),
                  }),
                })
              }
              data={activityGroups.map((group) => ({
                value: group,
                label: t(`activity.types.${group}`),
              }))}
              aria-label={t('activity.type')}
            />
            <Select
              placeholder={t('activity.allServers')}
              clearable
              w={200}
              value={server ?? null}
              onChange={(value) =>
                void navigate({ search: (prev) => ({ ...prev, server: value ?? undefined }) })
              }
              data={(instances.data ?? []).map((instance) => ({
                value: instance.id,
                label: instance.displayName,
              }))}
              aria-label={t('jobs.server')}
            />
          </Group>
        }
      />
      <Card maw={820}>
        {activity.isError ? (
          <ProblemAlert error={activity.error} onRetry={() => void activity.refetch()} />
        ) : items ? (
          <ActivityFeed items={items} />
        ) : (
          <Skeleton h={240} />
        )}
      </Card>
    </>
  );
}
