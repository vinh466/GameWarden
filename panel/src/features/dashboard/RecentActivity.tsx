import dayjs from 'dayjs';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Skeleton, Text, Timeline } from '@mantine/core';
import { queries } from '@/api/queries';
import { jobTypeLabel } from '@/components/JobProgress';
import { jobLook } from '@/features/jobs/jobLook';
import { SummaryCard } from './SummaryCard';

/** The latest few jobs; the Activity screen has the rest. */
export function RecentActivity({ limit = 4 }: { limit?: number }) {
  const { t } = useTranslation();
  const jobs = useQuery(queries.jobs());

  return (
    <SummaryCard title={t('dashboard.recentJobs')} to="/activity">
      {!jobs.data ? (
        <Skeleton h={120} />
      ) : jobs.data.length === 0 ? (
        <Text c="dimmed" size="sm">
          {t('activity.empty')}
        </Text>
      ) : (
        <Timeline bulletSize={20} lineWidth={2} mt="xs">
          {jobs.data.slice(0, limit).map((job) => {
            const look = jobLook[job.state];
            const Icon = look.icon;
            return (
              <Timeline.Item
                key={job.id}
                bullet={<Icon size={12} />}
                color={look.color}
                title={
                  <Text size="sm" fw={500}>
                    {jobTypeLabel(t, job.type)} · {job.instanceId ?? ''}
                  </Text>
                }
              >
                <Text size="xs" c="dimmed" lineClamp={1}>
                  {dayjs(job.createdAt).fromNow()} · {t(`jobs.state.${job.state}`)}
                  {job.message ? ` · ${job.message}` : ''}
                </Text>
              </Timeline.Item>
            );
          })}
        </Timeline>
      )}
    </SummaryCard>
  );
}
