import { useTranslation } from 'react-i18next';
import { Group, Progress, Stack, Text } from '@mantine/core';
import type { JobInfo } from '@/api/generated';

/** A running job: type, stage and progress; striped while the tool reports no percentage. */
export function JobProgress({ job }: { job: JobInfo }) {
  const { t } = useTranslation();
  const known = job.progress != null;
  return (
    <Stack gap={4}>
      <Group justify="space-between" gap="xs" wrap="nowrap">
        <Text size="xs" c="dimmed" truncate>
          {jobTypeLabel(t, job.type)} · {job.stage ?? t('jobs.state.queued')}
        </Text>
        {known && (
          <Text size="xs" fw={600} ff="monospace">
            {Math.round(job.progress ?? 0)}%
          </Text>
        )}
      </Group>
      <Progress
        value={known ? (job.progress ?? 0) : 100}
        striped={!known}
        animated={job.state === 'running'}
        size="sm"
        color="yellow"
        aria-label={`${jobTypeLabel(t, job.type)} ${known ? `${Math.round(job.progress ?? 0)}%` : ''}`}
      />
    </Stack>
  );
}

export function jobTypeLabel(
  t: (key: 'jobs.type.install' | 'jobs.type.update') => string,
  type: string
) {
  return type === 'install'
    ? t('jobs.type.install')
    : type === 'update'
      ? t('jobs.type.update')
      : type;
}
