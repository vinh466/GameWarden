import dayjs from 'dayjs';
import { useState } from 'react';
import { IconSearch } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Button,
  Card,
  Code,
  Drawer,
  Group,
  Select,
  SimpleGrid,
  Skeleton,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import type { SystemLogItem } from '@/api/generated';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';

export const Route = createFileRoute('/_app/system/logs')({
  component: Logs,
});

const levelColors: Record<string, string> = {
  trace: 'gray',
  debug: 'gray',
  info: 'blue',
  warn: 'yellow',
  error: 'red',
  critical: 'dark',
};

function Logs() {
  const { t } = useTranslation();
  const [level, setLevel] = useState<string | null>(null);
  const [component, setComponent] = useState<string | null>(null);
  const [instanceId, setInstanceId] = useState<string | null>(null);
  const [jobId, setJobId] = useState('');
  const [requestId, setRequestId] = useState('');
  const [window, setWindow] = useState('all');
  const [from, setFrom] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [paused, setPaused] = useState(false);
  const [limit, setLimit] = useState('100');
  const [selected, setSelected] = useState<SystemLogItem | null>(null);

  const filters = {
    ...(level ? { level } : {}),
    ...(component ? { component } : {}),
    ...(instanceId ? { instanceId } : {}),
    ...(jobId ? { jobId } : {}),
    ...(requestId ? { requestId } : {}),
    ...(from ? { from } : {}),
    ...(search ? { q: search } : {}),
  };
  const logs = useQuery(queries.systemLogs(filters, paused, Number(limit)));
  const summary = useQuery(queries.systemLogSummary(60));
  const components = useQuery(queries.systemLogComponents());
  const instances = useQuery(queries.instances());

  const rows = logs.data?.items ?? [];
  const errorCount = summary.data?.levels.find((item) => item.key === 'error')?.count ?? 0;
  const criticalCount = summary.data?.levels.find((item) => item.key === 'critical')?.count ?? 0;

  return (
    <Stack gap="lg">
      <Text c="dimmed" size="sm">
        {t('system.logs.description')}
      </Text>

      <SimpleGrid cols={{ base: 1, sm: 4 }}>
        <Summary label={t('system.logs.summaryTotal')} value={summary.data?.total} />
        <Summary label={t('system.logs.summaryErrors')} value={errorCount} color="red" />
        <Summary label={t('system.logs.summaryCritical')} value={criticalCount} color="dark" />
        <Summary
          label={t('system.logs.summaryWindow')}
          value={t('system.logs.minutes', { count: 60 })}
        />
      </SimpleGrid>

      <Card>
        <Stack gap="md">
          <Group align="flex-end" gap="sm" wrap="wrap">
            <Select
              label={t('system.logs.level')}
              placeholder={t('system.logs.allLevels')}
              clearable
              w={150}
              value={level}
              onChange={setLevel}
              data={(['trace', 'debug', 'info', 'warn', 'error', 'critical'] as const).map(
                (value) => ({
                  value,
                  label: t(`system.logs.levels.${value}`),
                })
              )}
            />
            <Select
              label={t('system.logs.component')}
              placeholder={t('system.logs.allComponents')}
              clearable
              searchable
              w={260}
              value={component}
              onChange={setComponent}
              data={(components.data ?? []).map((item) => ({
                value: item.component,
                label: item.component,
              }))}
            />
            <Select
              label={t('system.logs.instance')}
              placeholder={t('system.logs.allInstances')}
              clearable
              w={190}
              value={instanceId}
              onChange={setInstanceId}
              data={(instances.data ?? []).map((instance) => ({
                value: instance.id,
                label: instance.displayName,
              }))}
            />
            <TextInput
              label={t('system.logs.job')}
              placeholder={t('system.logs.jobPlaceholder')}
              w={190}
              value={jobId}
              onChange={(event) => setJobId(event.currentTarget.value)}
            />
            <TextInput
              label={t('system.logs.request')}
              placeholder={t('system.logs.requestPlaceholder')}
              w={190}
              value={requestId}
              onChange={(event) => setRequestId(event.currentTarget.value)}
            />
            <TextInput
              label={t('system.logs.search')}
              placeholder={t('system.logs.searchPlaceholder')}
              leftSection={<IconSearch size={16} />}
              w={240}
              value={search}
              onChange={(event) => setSearch(event.currentTarget.value)}
            />
            <Select
              label={t('system.logs.window')}
              w={150}
              value={window}
              onChange={(value) => {
                const next = value ?? 'all';
                setWindow(next);
                setFrom(
                  next === 'all'
                    ? undefined
                    : new Date(Date.now() - Number(next) * 60_000).toISOString()
                );
              }}
              data={[
                { value: 'all', label: t('system.logs.allTime') },
                { value: '15', label: t('system.logs.last15') },
                { value: '60', label: t('system.logs.last60') },
                { value: '1440', label: t('system.logs.last24') },
                { value: '10080', label: t('system.logs.last7') },
              ]}
            />
            <Select
              label={t('system.logs.rows')}
              w={110}
              value={limit}
              onChange={(value) => setLimit(value ?? '100')}
              data={['100', '250', '500']}
            />
            <Switch
              label={paused ? t('system.logs.paused') : t('system.logs.live')}
              checked={paused}
              onChange={(event) => setPaused(event.currentTarget.checked)}
            />
            <Button variant="light" onClick={() => void logs.refetch()} loading={logs.isFetching}>
              {t('actions.refresh')}
            </Button>
          </Group>

          {logs.isError ? (
            <ProblemAlert error={logs.error} onRetry={() => void logs.refetch()} />
          ) : logs.isLoading ? (
            <Skeleton h={320} />
          ) : rows.length === 0 ? (
            <Text c="dimmed" size="sm">
              {t('system.logs.empty')}
            </Text>
          ) : (
            <Table.ScrollContainer minWidth={920}>
              <Table highlightOnHover striped>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('system.logs.time')}</Table.Th>
                    <Table.Th>{t('system.logs.level')}</Table.Th>
                    <Table.Th>{t('system.logs.component')}</Table.Th>
                    <Table.Th>{t('system.logs.message')}</Table.Th>
                    <Table.Th>{t('system.logs.context')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {rows.map((row) => (
                    <Table.Tr
                      key={row.id}
                      onClick={() => setSelected(row)}
                      style={{ cursor: 'pointer' }}
                    >
                      <Table.Td>
                        <Text size="xs" ff="monospace" title={dayjs(row.at).format('LLL')}>
                          {dayjs(row.at).format('HH:mm:ss.SSS')}
                        </Text>
                        <Text size="xs" c="dimmed">
                          {dayjs(row.at).format('YYYY-MM-DD')}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Badge color={levelColors[row.level] ?? 'gray'} variant="light">
                          {row.level}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs" lineClamp={1} maw={220} title={row.component}>
                          {row.component}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm" lineClamp={2} title={row.message}>
                          {row.message}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Group gap={4}>
                          {row.instanceId && <Badge variant="outline">{row.instanceId}</Badge>}
                          {row.jobId && <Badge variant="outline">{row.jobId}</Badge>}
                          {row.requestId && (
                            <Badge variant="outline" title={row.requestId}>
                              {t('system.logs.request')}
                            </Badge>
                          )}
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
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

      <Drawer
        opened={selected !== null}
        onClose={() => setSelected(null)}
        position="right"
        size="lg"
        title={t('system.logs.details')}
      >
        {selected && <LogDetails item={selected} />}
      </Drawer>
    </Stack>
  );
}

function Summary({
  label,
  value,
  color,
}: {
  label: string;
  value: number | string | undefined;
  color?: string;
}) {
  return (
    <Card>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Title order={4} c={color}>
        {value ?? '—'}
      </Title>
    </Card>
  );
}

function LogDetails({ item }: { item: SystemLogItem }) {
  const { t } = useTranslation();
  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Badge color={levelColors[item.level] ?? 'gray'}>{item.level}</Badge>
        <Text size="xs" c="dimmed">
          #{item.id}
        </Text>
      </Group>
      <Text size="sm">{item.message}</Text>
      <Detail
        label={t('system.logs.time')}
        value={dayjs(item.at).format('YYYY-MM-DD HH:mm:ss.SSS')}
      />
      <Detail label={t('system.logs.component')} value={item.component} />
      <Detail label={t('system.logs.category')} value={item.category} />
      <Detail label={t('system.logs.source')} value={item.source} />
      {item.instanceId && <Detail label={t('system.logs.instance')} value={item.instanceId} />}
      {item.jobId && <Detail label={t('system.logs.job')} value={item.jobId} />}
      {item.requestId && <Detail label={t('system.logs.request')} value={item.requestId} />}
      {item.agentClientId && <Detail label={t('system.logs.agent')} value={item.agentClientId} />}
      {item.exception && (
        <Stack gap={4}>
          <Text size="xs" c="dimmed">
            {t('system.logs.exception')}
          </Text>
          <Code block>{item.exception}</Code>
        </Stack>
      )}
      {item.data && (
        <Stack gap={4}>
          <Text size="xs" c="dimmed">
            {t('system.logs.data')}
          </Text>
          <Code block>{pretty(item.data)}</Code>
        </Stack>
      )}
    </Stack>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <Group justify="space-between" gap="md" wrap="nowrap">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm" ta="right" style={{ wordBreak: 'break-all' }}>
        {value}
      </Text>
    </Group>
  );
}

function pretty(json: string) {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}
