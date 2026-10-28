import { IconAlertTriangle, IconPlugConnected } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Box, Card, Group, Stack, Text, Tooltip } from '@mantine/core';
import type { InstanceSnapshot, JobInfo } from '@/api/generated';
import { JobProgress } from '@/components/JobProgress';
import { AnchorLink } from '@/components/links';
import { BusyBadge, StatusBadge } from '@/components/StatusBadge';
import { portsLabel } from './ports';
import { ServerActions } from './ServerActions';

/** One server on the dashboard (docs/11 §4): status, ports, a running job, the last problem, quick actions. */
export function ServerCard({
  server,
  adapterName,
  job,
}: {
  server: InstanceSnapshot;
  adapterName: string;
  job?: JobInfo;
}) {
  const { t } = useTranslation();
  const ports = portsLabel(server);
  return (
    <Card>
      <Stack gap="sm" h="100%">
        <Group justify="space-between" align="flex-start" wrap="nowrap" gap="xs">
          {/* The name shrinks; the state badge never does (docs/11 §5: state is always readable). */}
          <Stack gap={0} miw={0} flex={1}>
            <AnchorLink
              to="/servers/$id"
              params={{ id: server.id }}
              fw={600}
              size="lg"
              c="var(--mantine-color-text)"
              truncate
            >
              {server.displayName}
            </AnchorLink>
            <Text size="xs" c="dimmed" ff="monospace" truncate>
              {adapterName} · {server.id}
            </Text>
          </Stack>
          <Group gap={6} wrap="nowrap" style={{ flexShrink: 0 }}>
            {/* With the job's progress bar below, a Busy badge would say the same thing twice. */}
            {server.activeJobId && job?.state !== 'running' && (
              <BusyBadge label={t('feedback.busyReason', { job: server.activeJobId })} />
            )}
            <StatusBadge state={server.state} />
          </Group>
        </Group>

        {ports && (
          <Group gap={6} c="dimmed">
            <IconPlugConnected size={14} aria-hidden />
            <Text size="sm" ff="monospace">
              {ports}
            </Text>
          </Group>
        )}

        {job && job.state === 'running' && <JobProgress job={job} />}

        {server.lastProblem && server.state !== 'running' && (
          <Tooltip label={server.lastProblem} multiline maw={360}>
            <Group gap={6} c="red" wrap="nowrap">
              <IconAlertTriangle size={14} aria-hidden />
              <Text size="sm" truncate>
                {server.lastProblem}
              </Text>
            </Group>
          </Tooltip>
        )}

        <Group justify="space-between" mt="auto" pt="xs" gap="xs">
          <Text size="xs" c="dimmed">
            {t('desired.label')}: {t(`desired.${server.desiredState}`)}
          </Text>
          {/* Longer labels (Vietnamese) wrap the actions to their own line; they stay on the right. */}
          <Box ml="auto">
            <ServerActions server={server} compact />
          </Box>
        </Group>
      </Stack>
    </Card>
  );
}
