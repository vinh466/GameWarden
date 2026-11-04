import { useState } from 'react';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button, Card, Group, Skeleton, Stack, Text } from '@mantine/core';
import { listSystemLogs, type SystemLogItem } from '@/api/generated';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';
import { LogDetailsDrawer } from './LogDetailsDrawer';
import { LogFilters, type LogFilterState } from './LogFilters';
import { LogSummaryCards } from './LogSummaryCards';
import { LogTable } from './LogTable';

/** The URL carries every filter, so Back and a pasted link restore the same view (docs/11 §4). */
export const logsSearchSchema = z.object({
  level: z.string().optional(),
  component: z.string().optional(),
  instanceId: z.string().optional(),
  jobId: z.string().optional(),
  requestId: z.string().optional(),
  window: z.enum(['all', '15', '60', '1440', '10080']).optional(),
  from: z.string().optional(),
  q: z.string().optional(),
  limit: z.enum(['100', '250', '500']).optional(),
});

type LogsSearch = z.infer<typeof logsSearchSchema>;

/** The queryable System log (docs/19): filters, a live table and one record in a drawer. */
export function LogsScreen() {
  const { t } = useTranslation();
  const searchParams = useSearch({ strict: false }) as LogsSearch;
  const navigate = useNavigate();
  const [paused, setPaused] = useState(false);
  const [selected, setSelected] = useState<SystemLogItem | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const filters: LogFilterState = {
    level: searchParams.level ?? null,
    component: searchParams.component ?? null,
    instanceId: searchParams.instanceId ?? null,
    jobId: searchParams.jobId ?? '',
    requestId: searchParams.requestId ?? '',
    window: searchParams.window ?? 'all',
    from: searchParams.from,
    search: searchParams.q ?? '',
    limit: searchParams.limit ?? '100',
    paused,
  };
  const update = (next: Partial<LogFilterState>) => {
    if (next.paused !== undefined) {
      setPaused(next.paused);
    }

    // `search` is the panel's name; the API and URL use `q`.
    const patch: LogsSearch = {
      ...(next.level !== undefined ? { level: next.level ?? undefined } : {}),
      ...(next.component !== undefined ? { component: next.component ?? undefined } : {}),
      ...(next.instanceId !== undefined ? { instanceId: next.instanceId ?? undefined } : {}),
      ...(next.jobId !== undefined ? { jobId: next.jobId || undefined } : {}),
      ...(next.requestId !== undefined ? { requestId: next.requestId || undefined } : {}),
      ...(next.window !== undefined ? { window: next.window as LogsSearch['window'] } : {}),
      ...(next.from !== undefined ? { from: next.from } : {}),
      ...(next.search !== undefined ? { q: next.search || undefined } : {}),
      ...(next.limit !== undefined ? { limit: next.limit as LogsSearch['limit'] } : {}),
    };
    void navigate({
      to: '/system/logs',
      search: (previous) => {
        const merged: Record<string, unknown> = { ...previous, ...patch };
        for (const [key, value] of Object.entries(merged)) {
          if (value === undefined || value === '') {
            delete merged[key];
          }
        }
        return merged as LogsSearch;
      },
      replace: true,
    });
  };
  const clear = () => {
    setPaused(false);
    void navigate({ to: '/system/logs', search: {}, replace: true });
  };

  const query = {
    ...(filters.level ? { level: filters.level } : {}),
    ...(filters.component ? { component: filters.component } : {}),
    ...(filters.instanceId ? { instanceId: filters.instanceId } : {}),
    ...(filters.jobId ? { jobId: filters.jobId } : {}),
    ...(filters.requestId ? { requestId: filters.requestId } : {}),
    ...(filters.from ? { from: filters.from } : {}),
    ...(filters.search ? { q: filters.search } : {}),
  };
  const base = queries.systemLogs(query, filters.paused, Number(filters.limit));
  const logs = useInfiniteQuery({
    queryKey: [...base.queryKey, 'infinite'],
    queryFn: ({ pageParam }) =>
      listSystemLogs({
        query: {
          ...query,
          limit: Number(filters.limit),
          ...(pageParam != null ? { before: pageParam } : {}),
        },
        throwOnError: true,
      }).then((response) => response.data!),
    initialPageParam: null as number | null,
    getNextPageParam: (last) => last.nextBefore,
    refetchInterval: base.refetchInterval,
  });
  const summary = useQuery(queries.systemLogSummary(60));
  const components = useQuery(queries.systemLogComponents());
  const instances = useQuery(queries.instances());
  const rows = logs.data?.pages.flatMap((page) => page.items) ?? [];
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
            onClear={clear}
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
              onClearFilters={clear}
            />
          )}
          <Group justify="space-between">
            <Text size="xs" c="dimmed">
              {t('system.logs.count', { count: rows.length })}
            </Text>
            {logs.hasNextPage && (
              <Button
                size="xs"
                variant="default"
                loading={logs.isFetchingNextPage}
                onClick={() => void logs.fetchNextPage()}
              >
                {t('system.logs.loadOlder')}
              </Button>
            )}
          </Group>
        </Stack>
      </Card>
      <LogDetailsDrawer item={selected} onClose={() => setSelected(null)} />
    </SystemTab>
  );
}
