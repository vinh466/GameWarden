import dayjs from 'dayjs';
import { useState } from 'react';
import { IconFileText } from '@tabler/icons-react';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Badge, Box, Progress, Stack, Text } from '@mantine/core';
import type { JobInfo } from '@/api/generated';
import { jobTypeLabel } from '@/components/JobProgress';
import { AnchorLink } from '@/components/links';
import { JobLogDrawer } from './JobLogDrawer';
import { jobLook } from './jobLook';
import { JobsMobileList } from './JobsMobileList';

/** Jobs with live progress; a row opens its log (docs/11 §2). */
export function JobsTable({
  jobs,
  loading,
  showServer = true,
}: {
  jobs: JobInfo[];
  loading: boolean;
  showServer?: boolean;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState<string | null>(null);

  return (
    <>
      <JobsMobileList jobs={jobs} showServer={showServer} onOpen={setOpen} />
      <Box visibleFrom="sm">
        <DataTable
          minHeight={jobs.length === 0 ? 160 : undefined}
          fetching={loading}
          records={jobs}
          idAccessor="id"
          noRecordsText={t('jobs.empty')}
          highlightOnHover
          onRowClick={({ record }) => setOpen(record.id)}
          columns={[
            {
              accessor: 'type',
              title: t('jobs.title'),
              render: (job) => (
                <Stack gap={0}>
                  <Text size="sm" fw={500}>
                    {jobTypeLabel(t, job.type)}
                  </Text>
                  <Text size="xs" c="dimmed" ff="monospace" visibleFrom="sm">
                    {job.id}
                  </Text>
                </Stack>
              ),
            },
            ...(showServer
              ? [
                  {
                    accessor: 'instanceId' as const,
                    title: t('jobs.server'),
                    width: 110,
                    render: (job: JobInfo) =>
                      job.instanceId ? (
                        <AnchorLink
                          to="/servers/$id"
                          params={{ id: job.instanceId }}
                          size="sm"
                          onClick={(event) => event.stopPropagation()}
                        >
                          {job.instanceId}
                        </AnchorLink>
                      ) : (
                        '—'
                      ),
                  },
                ]
              : []),
            {
              accessor: 'state',
              title: t('server.status'),
              width: 130,
              render: (job) => (
                <Badge color={jobLook[job.state].color}>{t(`jobs.state.${job.state}`)}</Badge>
              ),
            },
            {
              accessor: 'stage',
              title: t('jobs.stage'),
              visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
              render: (job) =>
                job.state === 'running' ? (
                  <Stack gap={4} miw={140}>
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
                  <Text
                    size="sm"
                    c={job.state === 'failed' ? 'red' : 'dimmed'}
                    lineClamp={1}
                    maw={360}
                  >
                    {job.message ?? job.stage}
                  </Text>
                ),
            },
            {
              accessor: 'createdAt',
              title: t('jobs.started'),
              textAlign: 'right',
              render: (job) => (
                <Text
                  size="sm"
                  c="dimmed"
                  title={dayjs(job.createdAt).format('LLL')}
                  style={{ whiteSpace: 'nowrap' }}
                >
                  {dayjs(job.createdAt).fromNow()}
                </Text>
              ),
            },
            {
              accessor: 'actions',
              title: '',
              width: 48,
              textAlign: 'right',
              render: (job) => (
                <ActionIcon
                  variant="subtle"
                  aria-label={t('jobs.openLog')}
                  onClick={(event) => {
                    event.stopPropagation();
                    setOpen(job.id);
                  }}
                >
                  <IconFileText size={16} />
                </ActionIcon>
              ),
            },
          ]}
        />
      </Box>
      <JobLogDrawer jobId={open} onClose={() => setOpen(null)} />
    </>
  );
}
