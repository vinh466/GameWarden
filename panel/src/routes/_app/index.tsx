import dayjs from 'dayjs';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Stack } from '@mantine/core';
import { useLocalStorage } from '@mantine/hooks';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';
import { AttentionCard } from '@/features/dashboard/AttentionCard';
import { GettingStarted } from '@/features/dashboard/GettingStarted';
import { NowLine } from '@/features/dashboard/NowLine';
import { OverviewStats } from '@/features/dashboard/OverviewStats';
import { RecentActivity } from '@/features/dashboard/RecentActivity';
import { ServersSummary } from '@/features/dashboard/ServersSummary';
import classes from '@/features/dashboard/Dashboard.module.css';

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

/** Monitoring at a glance (docs/11 §4, docs/22 §7.1): the "Now" line, then summaries that link to their screens. */
function Dashboard() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());
  const instances = useQuery(queries.instances());
  const [gettingStartedDismissed, setGettingStartedDismissed] = useLocalStorage({
    key: 'gamewarden.dashboard.gettingStarted',
    defaultValue: false,
  });
  const onboarding = instances.data?.length === 0 && !gettingStartedDismissed;

  return (
    <>
      <PageHeader
        title={t('dashboard.title')}
        description={
          system.data &&
          t('system.running', {
            version: system.data.version,
            since: dayjs(system.data.startedAt).fromNow(true),
          })
        }
      />
      <Stack gap="lg">
        <NowLine showEmptyAction={!onboarding} showProblemAction={!onboarding} />
        {onboarding && system.data ? (
          <GettingStarted system={system.data} onDismiss={() => setGettingStartedDismissed(true)} />
        ) : (
          <div className={classes.grid}>
            <div className={classes.stats}>
              <OverviewStats />
            </div>
            <div className={classes.servers}>
              <ServersSummary />
            </div>
            <div className={classes.attention} id="attention">
              <AttentionCard />
            </div>
            <div className={classes.activity}>
              <RecentActivity />
            </div>
          </div>
        )}
      </Stack>
    </>
  );
}
