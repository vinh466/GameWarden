import { IconPlus, IconServer2 } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Badge, ColorSwatch, Loader, Skeleton, Stack } from '@mantine/core';
import type { InstanceSnapshot, JobInfo } from '@/api/generated';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { jobTypeLabel } from '@/components/JobProgress';
import { ButtonLink, NavLinkLink } from '@/components/links';
import { stateLook, StatusBadge } from '@/components/StatusBadge';
import { portsLabel } from '@/features/servers/ports';
import { SummaryCard } from './SummaryCard';

/** One line per server: status, game, ports, a running job. Actions live on the Servers screen. */
export function ServersSummary() {
  const { t } = useTranslation();
  const instances = useQuery(queries.instances());
  const adapters = useQuery(queries.adapters());
  const jobs = useQuery(queries.jobs());
  const adapterName = (id: string) =>
    adapters.data?.adapters.find((adapter) => adapter.id === id)?.name ?? id;

  return (
    <SummaryCard title={t('dashboard.servers')} to="/servers">
      {!instances.data ? (
        <Skeleton h={120} />
      ) : instances.data.length === 0 ? (
        <EmptyState
          icon={IconServer2}
          title={t('dashboard.noServers')}
          text={t('dashboard.gettingStartedText')}
          action={
            <ButtonLink
              to="/servers"
              search={{ new: true }}
              leftSection={<IconPlus size={16} />}
              mt="sm"
            >
              {t('nav.newServer')}
            </ButtonLink>
          }
        />
      ) : (
        <Stack gap={2}>
          {instances.data.map((server) => (
            <NavLinkLink
              key={server.id}
              to="/servers/$id"
              params={{ id: server.id }}
              label={server.displayName}
              description={[adapterName(server.adapterId), portsLabel(server)]
                .filter(Boolean)
                .join(' · ')}
              leftSection={
                <ColorSwatch
                  color={`var(--mantine-color-${stateLook[server.state].color}-filled)`}
                  size={10}
                  withShadow={false}
                />
              }
              rightSection={
                <ServerState
                  server={server}
                  job={jobs.data?.find((job) => job.id === server.activeJobId)}
                />
              }
            />
          ))}
        </Stack>
      )}
    </SummaryCard>
  );
}

function ServerState({ server, job }: { server: InstanceSnapshot; job?: JobInfo }) {
  const { t } = useTranslation();
  if (job?.state === 'running') {
    return (
      <Badge color="yellow" leftSection={<Loader size={10} color="yellow" />}>
        {jobTypeLabel(t, job.type)}
        {job.progress != null ? ` ${Math.round(job.progress)}%` : ''}
      </Badge>
    );
  }

  return <StatusBadge state={server.state} />;
}
