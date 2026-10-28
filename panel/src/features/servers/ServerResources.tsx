import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Card, Group, SimpleGrid, Stack, Text, Title } from '@mantine/core';
import { queries } from '@/api/queries';
import { formatRam, MetricSparkline } from '@/components/MetricSparkline';
import { usageColor } from '@/components/ResourceMeter';

/** CPU and RAM of one running server's process tree over the last ten minutes (docs/11 §15 D6). */
export function ServerResources({ id }: { id: string }) {
  const { t } = useTranslation();
  const metrics = useQuery(queries.metrics());
  const series = metrics.data?.instances[id];

  return (
    <Card>
      <Title order={5} mb="sm">
        {t('server.resources')}
      </Title>
      {!series?.latest ? (
        <Text size="sm" c="dimmed">
          {t('server.resourcesIdle')}
        </Text>
      ) : (
        <SimpleGrid cols={2} spacing="lg">
          <Stack gap={4}>
            <Group justify="space-between">
              <Text size="sm" c="dimmed">
                {t('server.cpuShare')}
              </Text>
              <Text size="sm" fw={600} ff="monospace">
                {Math.round(series.latest.cpuPercent)}%
              </Text>
            </Group>
            <MetricSparkline
              history={series.history}
              field="cpuPercent"
              color={usageColor(series.latest.cpuPercent)}
              h={48}
              label={t('server.cpuShare')}
            />
          </Stack>
          <Stack gap={4}>
            <Group justify="space-between">
              <Text size="sm" c="dimmed">
                {t('server.ram')}
              </Text>
              <Text size="sm" fw={600} ff="monospace">
                {formatRam(series.latest.ramMb)}
              </Text>
            </Group>
            <MetricSparkline
              history={series.history}
              field="ramMb"
              color="violet"
              h={48}
              label={t('server.ram')}
            />
          </Stack>
        </SimpleGrid>
      )}
    </Card>
  );
}
