import { IconCpu, IconDatabase, IconListCheck, IconServer2, IconStack2 } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { SimpleGrid, Skeleton, Text } from '@mantine/core';
import type { InstanceSnapshot } from '@/api/generated';
import { queries } from '@/api/queries';
import { MetricSparkline } from '@/components/MetricSparkline';
import { usageColor, usagePercent } from '@/components/ResourceMeter';
import { failedRecently } from './attention';
import { StatCard } from './StatCard';

const gb = (mb: number) => (mb / 1024).toFixed(1);

/**
 * The dashboard's overview numbers (docs/11 §4, D7): servers, CPU and memory with the last ten minutes, disk, jobs.
 */
export function OverviewStats() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());
  const instances = useQuery(queries.instances());
  const jobs = useQuery(queries.jobs());
  const metrics = useQuery(queries.metrics());
  if (!system.data || !instances.data || !jobs.data) {
    return (
      <SimpleGrid cols={{ base: 1, xs: 2, md: 3, lg: 5 }}>
        {[1, 2, 3, 4, 5].map((key) => (
          <Skeleton key={key} h={132} />
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
  const history = metrics.data?.machine.history ?? [];
  const latest = metrics.data?.machine.latest;
  const cpu = latest?.cpuPercent;
  const ramUsed = latest?.ramMb ?? machine.ramTotalMb - machine.ramFreeMb;
  const ramPercent = usagePercent(ramUsed, machine.ramTotalMb);
  const now = Date.now();
  const activeJobs = jobs.data.filter((job) => job.state === 'running' || job.state === 'queued');
  const failed = jobs.data.filter((job) => failedRecently(job, now)).length;

  return (
    <SimpleGrid cols={{ base: 1, xs: 2, md: 3, lg: 5 }}>
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
        title={t('dashboard.stats.cpu')}
        icon={IconCpu}
        value={cpu == null ? '—' : `${Math.round(cpu)}%`}
        unit={<Unit>{t('dashboard.stats.cores', { cores: machine.cpuCores })}</Unit>}
        footer={t('dashboard.stats.lastMinutes')}
      >
        <MetricSparkline
          history={history}
          field="cpuPercent"
          color={usageColor(cpu ?? 0)}
          label={t('dashboard.stats.cpu')}
        />
      </StatCard>
      <StatCard
        to="/system"
        title={t('dashboard.stats.memory')}
        icon={IconStack2}
        value={`${ramPercent}%`}
        unit={<Unit>{t('dashboard.stats.ofTotal', { total: gb(machine.ramTotalMb) })}</Unit>}
        footer={t('dashboard.stats.usedFree', {
          used: gb(ramUsed),
          free: gb(machine.ramTotalMb - ramUsed),
        })}
      >
        <MetricSparkline
          history={history}
          field="ramMb"
          color={usageColor(ramPercent)}
          label={t('dashboard.stats.memory')}
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
