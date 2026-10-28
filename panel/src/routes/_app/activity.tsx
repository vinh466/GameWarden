import dayjs from 'dayjs';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Card, Text, Timeline } from '@mantine/core';
import { queries } from '@/api/queries';
import { jobTypeLabel } from '@/components/JobProgress';
import { PageHeader } from '@/components/PageHeader';
import { jobLook } from '@/features/jobs/jobLook';

// The feed of docs/11 §2. Until Core records events (phase 3, `events` table), jobs are what it can show.
export const Route = createFileRoute('/_app/activity')({
  loader: ({ context: { queryClient } }) =>
    queryClient.query({ ...queries.jobs(), staleTime: 'static' }),
  component: Activity,
});

function Activity() {
  const { t } = useTranslation();
  const jobs = useQuery(queries.jobs());

  return (
    <>
      <PageHeader title={t('activity.title')} />
      <Card maw={820}>
        {jobs.data && jobs.data.length > 0 ? (
          <Timeline bulletSize={24} lineWidth={2}>
            {jobs.data.map((job) => {
              const look = jobLook[job.state];
              const Icon = look.icon;
              return (
                <Timeline.Item
                  key={job.id}
                  bullet={<Icon size={13} />}
                  color={look.color}
                  title={`${jobTypeLabel(t, job.type)} · ${job.instanceId ?? ''}`}
                >
                  <Text size="sm" c={job.state === 'failed' ? 'red' : 'dimmed'}>
                    {t(`jobs.state.${job.state}`)}
                    {job.message ? ` — ${job.message}` : ''}
                  </Text>
                  <Text size="xs" c="dimmed" mt={2} title={dayjs(job.createdAt).format('LLL')}>
                    {dayjs(job.createdAt).fromNow()}
                  </Text>
                </Timeline.Item>
              );
            })}
          </Timeline>
        ) : (
          <Text c="dimmed">{t('activity.empty')}</Text>
        )}
      </Card>
    </>
  );
}
