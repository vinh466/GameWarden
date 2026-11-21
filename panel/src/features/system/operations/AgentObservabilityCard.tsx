import { IconActivity, IconRefresh } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Card, Group, Paper, SimpleGrid, Stack, Text, Title } from '@mantine/core';
import { queries } from '@/api/queries';
import { formatLatency, formatTokens } from './agentFormat';
import { ModelCalls } from './ModelCalls';

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
      <ModelCalls calls={calls} />
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
    <Stack gap={2} mt="xs">
      <svg
        width="100%"
        height={40}
        viewBox="0 0 100 100"
        preserveAspectRatio="none"
        role="img"
        aria-label={label}
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
      <Text size="xs" c="dimmed">
        {label}
      </Text>
    </Stack>
  );
}
