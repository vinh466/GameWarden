import { IconActivity, IconRefresh } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Button,
  Card,
  Code,
  Group,
  Paper,
  ScrollArea,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core';
import { modals } from '@mantine/modals';
import type { AgentModelCallRecord } from '@/api/generated';
import { queries } from '@/api/queries';

/** Bot and model observability (docs/21 §2.9): turns, latency, tokens and the redacted request/response previews. */
export function AgentObservabilityCard() {
  const { t } = useTranslation();
  const usage = useQuery(queries.agentUsage(24));
  const data = usage.data;
  const calls = data?.recentCalls ?? [];
  const latencies = [...calls].reverse().map((call) => call.latencyMs ?? 0);

  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Group gap="xs">
          <IconActivity size={20} />
          <Title order={5}>{t('system.operations.agentUsage')}</Title>
        </Group>
        <Button
          variant="subtle"
          size="xs"
          leftSection={<IconRefresh size={14} />}
          loading={usage.isFetching}
          onClick={() => void usage.refetch()}
        >
          {t('actions.refresh')}
        </Button>
      </Group>

      <SimpleGrid cols={{ base: 2, sm: 4 }} spacing="sm">
        <Metric label={t('system.operations.agentTurns')} value={data?.turns ?? 0} />
        <Metric label={t('system.operations.agentModelCalls')} value={data?.modelCalls ?? 0} />
        <Metric
          label={t('system.operations.agentErrors')}
          value={data?.modelErrors ?? 0}
          color={data?.modelErrors ? 'red' : undefined}
        />
        <Metric
          label={t('system.operations.agentLatency')}
          value={formatLatency(data?.avgLatencyMs)}
        />
        <Metric label={t('system.operations.agentTokens')} value={formatTokens(data)} />
        <Metric
          label={t('system.operations.agentMaxLatency')}
          value={formatLatency(data?.maxLatencyMs)}
        />
        <Metric label={t('system.operations.agentPending')} value={data?.pendingApprovals ?? 0} />
        <Metric
          label={t('system.operations.agentOpenIncidents')}
          value={data?.openIncidents ?? 0}
        />
      </SimpleGrid>

      <Sparkline values={latencies} label={t('system.operations.agentLatency')} />

      <Title order={6} mt="md" mb="xs">
        {t('system.operations.agentRecentCalls')}
      </Title>
      {calls.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t('system.operations.agentNoCalls')}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={720}>
          <Table verticalSpacing="xs" highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('system.operations.agentTime')}</Table.Th>
                <Table.Th>{t('system.operations.agentPurpose')}</Table.Th>
                <Table.Th>{t('system.operations.agentModel')}</Table.Th>
                <Table.Th>{t('system.operations.agentCallLatency')}</Table.Th>
                <Table.Th>{t('system.operations.agentTokens')}</Table.Th>
                <Table.Th>{t('system.operations.agentTools')}</Table.Th>
                <Table.Th>{t('system.operations.agentStatus')}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {calls.map((call, index) => (
                <Table.Tr key={call.id ?? index}>
                  <Table.Td>
                    <Text size="xs">{call.at ? new Date(call.at).toLocaleTimeString() : ''}</Text>
                  </Table.Td>
                  <Table.Td>{call.purpose ?? ''}</Table.Td>
                  <Table.Td>{call.model ?? ''}</Table.Td>
                  <Table.Td>{formatLatency(call.latencyMs)}</Table.Td>
                  <Table.Td>{(call.promptTokens ?? 0) + (call.completionTokens ?? 0)}</Table.Td>
                  <Table.Td>{call.toolCalls ?? 0}</Table.Td>
                  <Table.Td>
                    <Badge color={call.status === 'error' ? 'red' : 'green'} variant="light">
                      {call.status ?? 'ok'}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Button size="xs" variant="subtle" onClick={() => showCall(call, t)}>
                      {t('system.operations.agentDetails')}
                    </Button>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Card>
  );
}

function Metric({
  label,
  value,
  color,
}: {
  label: string;
  value: string | number;
  color?: string;
}) {
  return (
    <Paper withBorder p="sm" radius="md">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={600} c={color}>
        {value}
      </Text>
    </Paper>
  );
}

function Sparkline({ values, label }: { values: number[]; label: string }) {
  if (values.length < 2) {
    return null;
  }

  const top = Math.max(1, ...values) * 1.15;
  const points = values
    .map(
      (value, index) =>
        `${((index / (values.length - 1)) * 100).toFixed(2)},${(100 - (value / top) * 100).toFixed(2)}`
    )
    .join(' ');

  return (
    <svg
      width="100%"
      height={40}
      viewBox="0 0 100 100"
      preserveAspectRatio="none"
      role="img"
      aria-label={label}
      style={{ marginTop: 8 }}
    >
      <polyline
        points={points}
        fill="none"
        stroke="var(--mantine-color-blue-5)"
        strokeWidth={1.5}
        vectorEffect="non-scaling-stroke"
        strokeLinejoin="round"
      />
    </svg>
  );
}

function showCall(call: AgentModelCallRecord, t: TFunction) {
  modals.open({
    title: `${t('system.operations.agentCallDetail')} · ${call.purpose ?? ''}`,
    size: 'xl',
    children: (
      <Stack>
        <Group gap="xs">
          <Badge color={call.status === 'error' ? 'red' : 'green'}>{call.status ?? 'ok'}</Badge>
          <Text size="sm">{call.model}</Text>
          <Text size="sm" c="dimmed">
            {formatLatency(call.latencyMs)} ·{' '}
            {(call.promptTokens ?? 0) + (call.completionTokens ?? 0)} tokens
          </Text>
        </Group>
        {call.error && (
          <Text size="sm" c="red">
            {call.error}
          </Text>
        )}
        <Text size="sm" fw={600}>
          {t('system.operations.agentPrompt')}
        </Text>
        <ScrollArea h={220}>
          <Code block style={{ whiteSpace: 'pre-wrap' }}>
            {call.promptPreview ?? '—'}
          </Code>
        </ScrollArea>
        <Text size="sm" fw={600}>
          {t('system.operations.agentResponse')}
        </Text>
        <ScrollArea h={220}>
          <Code block style={{ whiteSpace: 'pre-wrap' }}>
            {call.responsePreview ?? '—'}
          </Code>
        </ScrollArea>
        {call.traceId && (
          <Text size="xs" c="dimmed">
            trace: {call.traceId}
          </Text>
        )}
      </Stack>
    ),
  });
}

function formatLatency(value?: number) {
  if (value === undefined || value === null) {
    return '—';
  }
  return value < 1000 ? `${Math.round(value)} ms` : `${(value / 1000).toFixed(1)} s`;
}

function formatTokens(data?: { promptTokens?: number; completionTokens?: number }) {
  const total = (data?.promptTokens ?? 0) + (data?.completionTokens ?? 0);
  return total < 1000 ? String(total) : `${(total / 1000).toFixed(1)}k`;
}
