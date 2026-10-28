import dayjs from 'dayjs';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Grid, Stack } from '@mantine/core';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';
import { AttentionCard } from '@/features/dashboard/AttentionCard';
import { OverviewStats } from '@/features/dashboard/OverviewStats';
import { RecentActivity } from '@/features/dashboard/RecentActivity';
import { ServersSummary } from '@/features/dashboard/ServersSummary';

export const Route = createFileRoute('/_app/')({
  loader: ({ context: { queryClient } }) =>
    Promise.all([
      queryClient.query({ ...queries.system(), staleTime: 'static' }),
      queryClient.query({ ...queries.instances(), staleTime: 'static' }),
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
      queryClient.query({ ...queries.jobs(), staleTime: 'static' }),
    ]),
  component: Dashboard,
});

/** Monitoring at a glance (docs/11 §4): overview numbers, then short summaries that link to their screens. */
function Dashboard() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());

  return (
    <>
      <PageHeader
        title={t('dashboard.title')}
        description={
          system.data &&
          t('system.running', {
            version: system.data.version,
            since: dayjs(system.data.startedAt).fromNow(),
          })
        }
      />
      <Stack gap="lg">
        <OverviewStats />
        <Grid gap="lg">
          <Grid.Col span={{ base: 12, md: 7 }}>
            <ServersSummary />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 5 }}>
            <Stack gap="lg">
              <AttentionCard />
              <RecentActivity />
            </Stack>
          </Grid.Col>
        </Grid>
      </Stack>
    </>
  );
}
