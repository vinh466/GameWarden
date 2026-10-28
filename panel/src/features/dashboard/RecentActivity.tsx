import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Skeleton } from '@mantine/core';
import { queries } from '@/api/queries';
import { ActivityFeed } from '@/features/activity/ActivityFeed';
import { SummaryCard } from './SummaryCard';

/** The latest few lines of the activity feed; the Activity screen has the rest. */
export function RecentActivity({ limit = 5 }: { limit?: number }) {
  const { t } = useTranslation();
  const activity = useQuery(queries.activity());

  return (
    <SummaryCard title={t('dashboard.recentJobs')} to="/activity">
      {activity.data ? (
        <ActivityFeed items={activity.data.slice(0, limit)} compact />
      ) : (
        <Skeleton h={120} />
      )}
    </SummaryCard>
  );
}
