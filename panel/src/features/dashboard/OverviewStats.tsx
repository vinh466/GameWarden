import { IconCpu, IconDatabase, IconListCheck, IconServer2 } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Progress, SimpleGrid, Skeleton, Text } from '@mantine/core';
import type { InstanceSnapshot } from '@/api/generated';
import { queries } from '@/api/queries';
import { usageColor, usagePercent } from '@/components/ResourceMeter';
import { failedRecently } from './attention';
import { StatCard } from './StatCard';

const gb = (mb: number) => (mb / 1024).toFixed(1);

/**
 * The dashboard's overview numbers (docs/11 §4): servers, memory, disk, jobs. CPU load and per-server metrics join
 * when Core reports them (M2 phase 3).
 */
export function OverviewStats() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());
  const instances = useQuery(queries.instances());
  const jobs = useQuery(queries.jobs());
  if (!system.data || !instances.data || !jobs.data) {
    return (
      <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }}>
        {[1, 2, 3, 4].map((key) => (
          <Skeleton key={key} h={124} />
        ))}
      </SimpleGrid>
    );
  }

  const { machine, dataRoot } = system.data;
  const servers = instances.data;
  const running = servers.filter((server) => server.state === 'running').length;
  const notRunning = new Map<InstanceSnapshot['state'], number>();
  for (const server of servers) {
    if (server.state !== 'running') {
      notRunning.set(server.state, (notRunning.get(server.state) ?? 0) + 1);
    }
  }
  const others = [...notRunning]
    .map(([state, count]) => `${t(`state.${state}`)} ${count}`)
    .join(' · ');
  const ramUsed = machine.ramTotalMb - machine.ramFreeMb;
  const ramPercent = usagePercent(ramUsed, machine.ramTotalMb);
  const now = Date.now();
  const activeJobs = jobs.data.filter((job) => job.state === 'running' || job.state === 'queued');
  const failed = jobs.data.filter((job) => failedRecently(job, now)).length;

  return (
    <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }}>
      <StatCard
        to="/servers"
        title={t('dashboard.stats.servers')}
        icon={IconServer2}
        value={`${running} / ${servers.length}`}
        unit={<Unit>{t('dashboard.stats.running')}</Unit>}
        footer={others || '—'}
      />
      <StatCard
        to="/system"
        title={t('dashboard.stats.memory')}
        icon={IconCpu}
        value={`${gb(ramUsed)} / ${gb(machine.ramTotalMb)} GB`}
        unit={
          <Text c={usageColor(ramPercent)} fz="sm" fw={600} lh={1}>
            {ramPercent}%
          </Text>
        }
        footer={`${t('dashboard.stats.ramFree', { free: gb(machine.ramFreeMb) })} · ${t('dashboard.cpuCores', { count: machine.cpuCores })}`}
      >
        <Progress
          value={ramPercent}
          color={usageColor(ramPercent)}
          size="sm"
          mt="sm"
          aria-label={`${t('dashboard.ram')} ${ramPercent}%`}
        />
      </StatCard>
      <StatCard
        to="/system"
        title={t('dashboard.stats.disk')}
        icon={IconDatabase}
        value={`${gb(machine.diskFreeMb)} GB`}
        unit={<Unit>{t('dashboard.stats.free')}</Unit>}
        footer={t('dashboard.stats.diskOn', { root: dataRoot })}
      />
      <StatCard
        to="/jobs"
        title={t('dashboard.stats.jobs')}
        icon={IconListCheck}
        value={activeJobs.length}
        unit={<Unit>{t('dashboard.stats.running')}</Unit>}
        footer={
          failed > 0 ? (
            <Text span inherit c="red">
              {t('dashboard.stats.failed', { failed })}
            </Text>
          ) : (
            t('dashboard.stats.noFailures')
          )
        }
      />
    </SimpleGrid>
  );
}

function Unit({ children }: { children: string }) {
  return (
    <Text c="dimmed" fz="sm" lh={1}>
      {children}
    </Text>
  );
}
