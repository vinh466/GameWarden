import dayjs from 'dayjs';
import { IconActivity } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Card, Code, Group, SimpleGrid, Stack, Text, Title } from '@mantine/core';
import type { GameIntegrationInfo, GameIntegrationMetrics } from '@/api/generated';

function value(text: string | number | null | undefined) {
  return text === null || text === undefined || text === '' ? '—' : String(text);
}

/** The game's own view of itself (docs/16): name and version, then the live metrics the provider exposes. */
export function IntegrationSummary({
  serverName,
  info,
  metrics,
}: {
  serverName: string;
  info: GameIntegrationInfo | null;
  metrics: GameIntegrationMetrics | null;
}) {
  const { t } = useTranslation();
  const world = info?.worldName ?? info?.worldGuid;
  const hasInfo = !!info && !!(info.serverName || info.version || info.description || world);
  const metricItems = metrics
    ? [
        metrics.serverFps == null
          ? null
          : { key: 'fps', label: t('integration.metrics.fps'), text: value(metrics.serverFps) },
        metrics.currentPlayerNum == null && metrics.maxPlayerNum == null
          ? null
          : {
              key: 'players',
              label: t('integration.metrics.players'),
              text:
                metrics.currentPlayerNum == null
                  ? `— / ${value(metrics.maxPlayerNum)}`
                  : `${value(metrics.currentPlayerNum)}${metrics.maxPlayerNum == null ? '' : ` / ${value(metrics.maxPlayerNum)}`}`,
            },
        metrics.serverFrameTime == null
          ? null
          : {
              key: 'frameTime',
              label: t('integration.metrics.frameTime'),
              text: t('integration.metrics.millis', { value: metrics.serverFrameTime.toFixed(1) }),
            },
        metrics.uptime == null
          ? null
          : {
              key: 'uptime',
              label: t('integration.metrics.uptime'),
              text: dayjs.duration(metrics.uptime, 'seconds').humanize(),
            },
        metrics.baseCampNum == null
          ? null
          : {
              key: 'bases',
              label: t('integration.metrics.bases'),
              text: value(metrics.baseCampNum),
            },
        metrics.days == null
          ? null
          : { key: 'days', label: t('integration.metrics.days'), text: value(metrics.days) },
      ].filter((item) => !!item)
    : [];

  if (!hasInfo && metricItems.length === 0) {
    return null;
  }

  return (
    <SimpleGrid cols={{ base: 1, lg: hasInfo && metricItems.length > 0 ? 2 : 1 }}>
      {hasInfo && info && (
        <Card>
          <Stack gap="sm">
            <Group justify="space-between" align="flex-start" gap="sm">
              <Title order={5}>{info.serverName ?? serverName}</Title>
              {info.version && <Badge variant="outline">{value(info.version)}</Badge>}
            </Group>
            {info.description && (
              <Text size="sm" c="dimmed">
                {info.description}
              </Text>
            )}
            {world && (
              <Stack gap={4}>
                <Text size="xs" c="dimmed">
                  {t('integration.info.world')}
                </Text>
                <Code>{value(world)}</Code>
              </Stack>
            )}
          </Stack>
        </Card>
      )}
      {metricItems.length > 0 && (
        <Card>
          <Group gap="xs" mb="sm">
            <IconActivity size={18} aria-hidden />
            <Title order={5}>{t('integration.metrics.title')}</Title>
          </Group>
          <SimpleGrid cols={{ base: 2, sm: 3 }}>
            {metricItems.map((item) => (
              <Metric key={item!.key} label={item!.label} value={item!.text} />
            ))}
          </SimpleGrid>
        </Card>
      )}
    </SimpleGrid>
  );
}

function Metric({ label, value: shown }: { label: string; value: string }) {
  return (
    <Stack gap={2}>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={600}>{shown}</Text>
    </Stack>
  );
}
