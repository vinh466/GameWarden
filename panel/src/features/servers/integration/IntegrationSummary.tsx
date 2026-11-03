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
  if (!info && !metrics) {
    return null;
  }

  const title = info?.serverName ?? serverName;
  return (
    <SimpleGrid cols={{ base: 1, lg: 2 }}>
      {info && (
        <Card>
          <Stack gap="sm">
            <Group justify="space-between" align="flex-start" gap="sm">
              <Title order={5}>{title}</Title>
              {info.version && <Badge variant="outline">{value(info.version)}</Badge>}
            </Group>
            {info.description && (
              <Text size="sm" c="dimmed">
                {info.description}
              </Text>
            )}
            <Stack gap={4}>
              <Text size="xs" c="dimmed">
                {t('integration.info.version')}
              </Text>
              <Text size="sm">{value(info.version)}</Text>
              <Text size="xs" c="dimmed">
                {t('integration.info.world')}
              </Text>
              <Code>{value(info.worldGuid)}</Code>
            </Stack>
          </Stack>
        </Card>
      )}
      {metrics && (
        <Card>
          <Group gap="xs" mb="sm">
            <IconActivity size={18} aria-hidden />
            <Title order={5}>{t('integration.metrics.title')}</Title>
          </Group>
          <SimpleGrid cols={{ base: 2, sm: 3 }}>
            <Metric label={t('integration.metrics.fps')} value={value(metrics.serverFps)} />
            <Metric
              label={t('integration.metrics.players')}
              value={
                metrics.currentPlayerNum == null && metrics.maxPlayerNum == null
                  ? '—'
                  : `${value(metrics.currentPlayerNum)} / ${value(metrics.maxPlayerNum)}`
              }
            />
            <Metric
              label={t('integration.metrics.frameTime')}
              value={
                metrics.serverFrameTime == null
                  ? '—'
                  : t('integration.metrics.millis', {
                      value: metrics.serverFrameTime.toFixed(1),
                    })
              }
            />
            <Metric
              label={t('integration.metrics.uptime')}
              value={
                metrics.uptime == null ? '—' : dayjs.duration(metrics.uptime, 'seconds').humanize()
              }
            />
            <Metric label={t('integration.metrics.bases')} value={value(metrics.baseCampNum)} />
            <Metric label={t('integration.metrics.days')} value={value(metrics.days)} />
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
