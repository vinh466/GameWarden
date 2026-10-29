import dayjs from 'dayjs';
import { useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Badge, Code, Drawer, Group, ScrollArea, Skeleton, Stack, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { useHelpHost } from '@/components/help/useHelp';
import { JobProgress, jobTypeLabel } from '@/components/JobProgress';
import { jobLook } from './jobLook';

/** One job and its log; follows the log while the job runs. Steps aside for the help drawer (docs/12 §4). */
export function JobLogDrawer({ jobId, onClose }: { jobId: string | null; onClose: () => void }) {
  const { t } = useTranslation();
  const job = useQuery({ ...queries.job(jobId ?? ''), enabled: !!jobId });
  const log = useQuery({
    ...queries.jobLog(jobId ?? ''),
    enabled: !!jobId,
    refetchInterval: job.data?.state === 'running' ? 2_000 : false,
  });
  const viewport = useRef<HTMLDivElement>(null);
  const helpHost = useHelpHost();

  useEffect(() => {
    viewport.current?.scrollTo({ top: viewport.current.scrollHeight });
  }, [log.data?.lines.length]);

  return (
    <Drawer
      opened={!!jobId}
      onClose={onClose}
      position="right"
      size="xl"
      {...helpHost}
      title={
        job.data
          ? `${jobTypeLabel(t, job.data.type)} · ${job.data.instanceId ?? ''}`
          : t('jobs.log')
      }
    >
      {!job.data ? (
        <Skeleton h={200} />
      ) : (
        <Stack gap="md">
          <Group gap="sm">
            <Badge color={jobLook[job.data.state].color}>{t(`jobs.state.${job.data.state}`)}</Badge>
            <Text size="sm" c="dimmed">
              {dayjs(job.data.createdAt).format('LLL')}
              {job.data.finishedAt
                ? ` · ${dayjs.duration(dayjs(job.data.finishedAt).diff(job.data.createdAt)).humanize()}`
                : ''}
            </Text>
          </Group>
          {job.data.state === 'running' && <JobProgress job={job.data} />}
          {job.data.message && (
            <Text size="sm" c={job.data.state === 'failed' ? 'red' : undefined}>
              {job.data.message}
            </Text>
          )}
          <ScrollArea h="calc(100dvh - 260px)" viewportRef={viewport} type="auto">
            <Code block fz="xs">
              {(log.data?.lines ?? []).join('\n')}
            </Code>
          </ScrollArea>
        </Stack>
      )}
    </Drawer>
  );
}
