import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Card, Group, Skeleton, Stack, Text } from '@mantine/core';
import type { SystemLogItem } from '@/api/generated';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';
import { LogDetailsDrawer } from './LogDetailsDrawer';
import { LogFilters, type LogFilterState } from './LogFilters';
import { LogSummaryCards } from './LogSummaryCards';
import { LogTable } from './LogTable';

const initialFilters: LogFilterState = {
  level: null,
  component: null,
  instanceId: null,
  jobId: '',
  requestId: '',
  window: 'all',
  from: undefined,
  search: '',
  limit: '100',
  paused: false,
};

/** The queryable System log (docs/19): filters, a live table and one record in a drawer. */
export function LogsScreen() {
  const { t } = useTranslation();
  const [filters, setFilters] = useState<LogFilterState>(initialFilters);
  const [selected, setSelected] = useState<SystemLogItem | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const update = (next: Partial<LogFilterState>) =>
    setFilters((current) => ({ ...current, ...next }));

  const query = {
    ...(filters.level ? { level: filters.level } : {}),
    ...(filters.component ? { component: filters.component } : {}),
    ...(filters.instanceId ? { instanceId: filters.instanceId } : {}),
    ...(filters.jobId ? { jobId: filters.jobId } : {}),
    ...(filters.requestId ? { requestId: filters.requestId } : {}),
    ...(filters.from ? { from: filters.from } : {}),
    ...(filters.search ? { q: filters.search } : {}),
  };
  const logs = useQuery(queries.systemLogs(query, filters.paused, Number(filters.limit)));
  const summary = useQuery(queries.systemLogSummary(60));
  const components = useQuery(queries.systemLogComponents());
  const instances = useQuery(queries.instances());
  const rows = logs.data?.items ?? [];
  const hasFilters = Boolean(
    filters.level ||
    filters.component ||
    filters.instanceId ||
    filters.jobId ||
    filters.requestId ||
    filters.from ||
    filters.search
  );
  const refresh = async () => {
    setRefreshing(true);
    try {
      await logs.refetch();
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <SystemTab description={t('system.logs.description')}>
      <LogSummaryCards summary={summary.data} />
      <Card>
        <Stack gap="md">
          <LogFilters
            value={filters}
            onChange={update}
            onClear={() => setFilters(initialFilters)}
            components={components.data ?? []}
            instances={instances.data ?? []}
            refreshing={refreshing}
            onRefresh={() => void refresh()}
          />
          {logs.isError ? (
            <ProblemAlert error={logs.error} onRetry={() => void logs.refetch()} />
          ) : !logs.data ? (
            <Skeleton h={320} />
          ) : (
            <LogTable
              rows={rows}
              hasFilters={hasFilters}
              onSelect={setSelected}
              onClearFilters={() => setFilters(initialFilters)}
            />
          )}
          <Group justify="space-between">
            <Text size="xs" c="dimmed">
              {t('system.logs.count', { count: rows.length })}
            </Text>
            {logs.data?.nextBefore && (
              <Text size="xs" c="dimmed">
                {t('system.logs.more')}
              </Text>
            )}
          </Group>
        </Stack>
      </Card>
      <LogDetailsDrawer item={selected} onClose={() => setSelected(null)} />
    </SystemTab>
  );
}
