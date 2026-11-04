import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { Badge, Divider, Group, Progress, Stack, Text, UnstyledButton } from '@mantine/core';
import type { JobInfo } from '@/api/generated';
import { jobTypeLabel } from '@/components/JobProgress';
import { jobLook } from './jobLook';

/** Jobs as cards on phones, with the stage and progress the table hides (docs/11 §6.7). */
export function JobsMobileList({
  jobs,
  showServer,
  onOpen,
}: {
  jobs: JobInfo[];
  showServer: boolean;
  onOpen: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap={0} hiddenFrom="sm">
      {jobs.map((job, index) => (
        <div key={job.id}>
          <UnstyledButton
            w="100%"
            p="sm"
            onClick={() => onOpen(job.id)}
            aria-label={`${jobTypeLabel(t, job.type)} ${job.id}`}
          >
            <Stack gap={6} ta="left">
              <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
                <Stack gap={0} miw={0}>
                  <Text size="sm" fw={500}>
                    {jobTypeLabel(t, job.type)}
                  </Text>
                  <Text size="xs" c="dimmed" ff="monospace">
                    {job.id}
                  </Text>
                </Stack>
                <Badge color={jobLook[job.state].color}>{t(`jobs.state.${job.state}`)}</Badge>
              </Group>
              {showServer && job.instanceId && (
                <Text size="xs" c="dimmed">
                  {job.instanceId}
                </Text>
              )}
              {job.state === 'running' ? (
                <Stack gap={4}>
                  <Text size="xs" c="dimmed">
                    {job.stage}
                    {job.progress != null ? ` · ${Math.round(job.progress)}%` : ''}
                  </Text>
                  <Progress
                    size="xs"
                    color="yellow"
                    value={job.progress ?? 100}
                    striped={job.progress == null}
                    animated
                    aria-label={`${jobTypeLabel(t, job.type)} ${job.progress != null ? `${Math.round(job.progress)}%` : ''}`}
                  />
                </Stack>
              ) : (
                <Text size="xs" c={job.state === 'failed' ? 'red' : 'dimmed'} lineClamp={3}>
                  {job.message ?? job.stage}
                </Text>
              )}
              <Text
                size="xs"
                c="dimmed"
                component="time"
                dateTime={job.createdAt}
                title={dayjs(job.createdAt).format('LLL')}
              >
                {dayjs(job.createdAt).fromNow()}
              </Text>
            </Stack>
          </UnstyledButton>
          {index < jobs.length - 1 && <Divider />}
        </div>
      ))}
    </Stack>
  );
}
