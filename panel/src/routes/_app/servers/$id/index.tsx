import dayjs from 'dayjs';
import { IconAlertTriangle, IconCheck, IconCopy } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Card,
  CopyButton,
  Group,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Title,
  Button,
} from '@mantine/core';
import { queries } from '@/api/queries';
import { JobProgress, jobTypeLabel } from '@/components/JobProgress';
import { AnchorLink } from '@/components/links';
import { StatusBadge } from '@/components/StatusBadge';
import { jobLook } from '@/features/jobs/jobLook';
import { connectAddress } from '@/features/servers/ports';
import { ServerResources } from '@/features/servers/ServerResources';

export const Route = createFileRoute('/_app/servers/$id/')({
  component: Overview,
});

function Overview() {
  const { t } = useTranslation();
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const jobs = useQuery(queries.jobs(id));
  const adapters = useQuery(queries.adapters());
  if (!server.data) {
    return null;
  }

  const data = server.data;
  const adapter = adapters.data?.adapters.find((candidate) => candidate.id === data.adapterId);
  const address = connectAddress(data);
  const running = jobs.data?.find((job) => job.id === data.activeJobId);

  return (
    <Stack gap="lg">
      {data.lastProblem && data.state !== 'running' && (
        <Alert color="red" icon={<IconAlertTriangle aria-hidden />} title={t('server.lastProblem')}>
          {data.lastProblem}
        </Alert>
      )}
      {running && (
        <Card>
          <JobProgress job={running} />
        </Card>
      )}
      <ServerResources id={id} />
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Card>
          <Title order={5} mb="sm">
            {t('server.status')}
          </Title>
          <Table withRowBorders={false} highlightOnHover={false} verticalSpacing={6}>
            <Table.Tbody>
              <Row label={t('server.status')} value={<StatusBadge state={data.state} />} />
              <Row label={t('desired.label')} value={t(`desired.${data.desiredState}`)} />
              <Row
                label={t('server.adapter')}
                value={adapter ? `${adapter.name} (${adapter.level})` : data.adapterId}
              />
              <Row
                label={t('server.process')}
                value={
                  data.runnerPid ? (
                    <Text size="sm" ff="monospace">
                      {t('server.runner', { pid: data.runnerPid })}
                      {data.gamePid ? ` · ${t('server.game', { pid: data.gamePid })}` : ''}
                    </Text>
                  ) : (
                    t('server.notRunning')
                  )
                }
              />
              <Row
                label=""
                value={
                  data.recentRestarts > 0
                    ? t('server.restarts', { count: data.recentRestarts })
                    : t('server.noRestarts')
                }
              />
            </Table.Tbody>
          </Table>
        </Card>
        <Card>
          <Title order={5} mb={4}>
            {t('server.connect')}
          </Title>
          <Text size="sm" c="dimmed" mb="sm">
            {t('server.connectHint')}
          </Text>
          {address && (
            <Group gap="xs" mb="md" wrap="nowrap">
              <Text ff="monospace" fw={600} size="lg">
                {address}
              </Text>
              <CopyButton value={address}>
                {({ copied, copy }) => (
                  <Button
                    size="xs"
                    variant="light"
                    color={copied ? 'green' : undefined}
                    leftSection={copied ? <IconCheck size={14} /> : <IconCopy size={14} />}
                    onClick={copy}
                  >
                    {copied ? t('actions.copied') : t('actions.copy')}
                  </Button>
                )}
              </CopyButton>
            </Group>
          )}
          <Table withRowBorders={false} highlightOnHover={false} verticalSpacing={4}>
            <Table.Tbody>
              {Object.entries(data.ports).map(([name, port]) => (
                <Row
                  key={name}
                  label={name}
                  value={
                    <Text
                      size="sm"
                      ff="monospace"
                    >{`${port}/${adapter?.ports.find((p) => p.name === name)?.protocol ?? 'udp'}`}</Text>
                  }
                />
              ))}
            </Table.Tbody>
          </Table>
        </Card>
      </SimpleGrid>
      <Card>
        <Group justify="space-between" mb="sm">
          <Title order={5}>{t('server.recentJobs')}</Title>
          <AnchorLink to="/servers/$id/jobs" params={{ id }} size="sm">
            {t('actions.viewAll')}
          </AnchorLink>
        </Group>
        <Stack gap="xs">
          {(jobs.data ?? []).slice(0, 4).map((job) => {
            const look = jobLook[job.state];
            const Icon = look.icon;
            return (
              <Group key={job.id} gap="sm" wrap="nowrap">
                <Icon size={16} color={`var(--mantine-color-${look.color}-filled)`} aria-hidden />
                <Text size="sm" truncate>
                  {jobTypeLabel(t, job.type)} · {t(`jobs.state.${job.state}`)}
                  {job.message ? ` · ${job.message}` : ''}
                </Text>
                <Text size="xs" c="dimmed" ml="auto" style={{ whiteSpace: 'nowrap' }}>
                  {dayjs(job.createdAt).fromNow()}
                </Text>
              </Group>
            );
          })}
          {jobs.data?.length === 0 && (
            <Text size="sm" c="dimmed">
              {t('jobs.empty')}
            </Text>
          )}
        </Stack>
      </Card>
    </Stack>
  );
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <Table.Tr>
      <Table.Td w={140} c="dimmed" fz="sm">
        {label}
      </Table.Td>
      <Table.Td fz="sm">{value}</Table.Td>
    </Table.Tr>
  );
}
