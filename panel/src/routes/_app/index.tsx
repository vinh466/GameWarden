import dayjs from 'dayjs';
import { IconPlus, IconServer2 } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Card, Group, SimpleGrid, Skeleton, Stack, Text, Timeline, Title } from '@mantine/core';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { jobTypeLabel } from '@/components/JobProgress';
import { AnchorLink, ButtonLink } from '@/components/links';
import { PageHeader } from '@/components/PageHeader';
import { ProblemAlert } from '@/components/ProblemAlert';
import { ResourceMeter } from '@/components/ResourceMeter';
import { jobLook } from '@/features/jobs/jobLook';
import { ServerCard } from '@/features/servers/ServerCard';

export const Route = createFileRoute('/_app/')({
  loader: ({ context: { queryClient } }) =>
    Promise.all([
      queryClient.query({ ...queries.instances(), staleTime: 'static' }),
      queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
      queryClient.query({ ...queries.jobs(), staleTime: 'static' }),
    ]),
  component: Dashboard,
});

function Dashboard() {
  const { t } = useTranslation();
  const instances = useQuery(queries.instances());
  const adapters = useQuery(queries.adapters());
  const jobs = useQuery(queries.jobs());
  const adapterName = (id: string) =>
    adapters.data?.adapters.find((adapter) => adapter.id === id)?.name ?? id;
  const runningJob = (jobId: string | null) => jobs.data?.find((job) => job.id === jobId);

  return (
    <>
      <PageHeader
        title={t('dashboard.title')}
        actions={
          <ButtonLink to="/servers/new" leftSection={<IconPlus size={16} />}>
            {t('nav.newServer')}
          </ButtonLink>
        }
      />
      <Stack gap="xl">
        <MachineCard />

        <Stack gap="sm">
          <Title order={4}>{t('dashboard.servers')}</Title>
          {instances.isError ? (
            <ProblemAlert error={instances.error} onRetry={() => void instances.refetch()} />
          ) : !instances.data ? (
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
              <Skeleton h={170} />
              <Skeleton h={170} />
            </SimpleGrid>
          ) : instances.data.length === 0 ? (
            <Card>
              <EmptyState
                icon={IconServer2}
                title={t('dashboard.noServers')}
                text={t('dashboard.gettingStartedText')}
                action={
                  <ButtonLink to="/servers/new" leftSection={<IconPlus size={16} />} mt="sm">
                    {t('nav.newServer')}
                  </ButtonLink>
                }
              />
            </Card>
          ) : (
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
              {instances.data.map((server) => (
                <ServerCard
                  key={server.id}
                  server={server}
                  adapterName={adapterName(server.adapterId)}
                  job={runningJob(server.activeJobId)}
                />
              ))}
            </SimpleGrid>
          )}
        </Stack>

        <Stack gap="sm">
          <Group justify="space-between">
            <Title order={4}>{t('dashboard.recentJobs')}</Title>
            <AnchorLink to="/activity" size="sm">
              {t('actions.viewAll')}
            </AnchorLink>
          </Group>
          <Card>
            {jobs.data && jobs.data.length > 0 ? (
              <Timeline bulletSize={22} lineWidth={2}>
                {jobs.data.slice(0, 5).map((job) => {
                  const look = jobLook[job.state];
                  const Icon = look.icon;
                  return (
                    <Timeline.Item
                      key={job.id}
                      bullet={<Icon size={12} />}
                      color={look.color}
                      title={`${jobTypeLabel(t, job.type)} · ${job.instanceId ?? ''}`}
                    >
                      <Text size="xs" c="dimmed">
                        {dayjs(job.createdAt).fromNow()} · {t(`jobs.state.${job.state}`)}
                        {job.message ? ` · ${job.message}` : ''}
                      </Text>
                    </Timeline.Item>
                  );
                })}
              </Timeline>
            ) : (
              <Text c="dimmed" size="sm">
                {t('activity.empty')}
              </Text>
            )}
          </Card>
        </Stack>
      </Stack>
    </>
  );
}

function MachineCard() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());
  if (!system.data) {
    return <Skeleton h={96} />;
  }

  const { machine } = system.data;
  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Text fw={600}>{t('dashboard.vm')}</Text>
        <Text size="sm" c="dimmed">
          {t('dashboard.cpuCores', { count: machine.cpuCores })} ·{' '}
          {t('system.running', {
            version: system.data.version,
            since: dayjs(system.data.startedAt).fromNow(),
          })}
        </Text>
      </Group>
      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="lg">
        <ResourceMeter
          label={t('dashboard.ram')}
          used={machine.ramTotalMb - machine.ramFreeMb}
          total={machine.ramTotalMb}
          unit="MB"
        />
        <Stack gap={4}>
          <Text size="sm" c="dimmed">
            {t('dashboard.diskFree')}
          </Text>
          <Text fw={600} ff="monospace">
            {(machine.diskFreeMb / 1024).toFixed(1)} GB
          </Text>
        </Stack>
      </SimpleGrid>
    </Card>
  );
}
