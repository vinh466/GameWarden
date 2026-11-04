import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { Badge, Code, Drawer, Group, Stack, Text } from '@mantine/core';
import type { SystemLogItem } from '@/api/generated';
import { logLevelColors, prettyData } from './logs';

/** One log record, with the exception and structured data in full. */
export function LogDetailsDrawer({
  item,
  onClose,
}: {
  item: SystemLogItem | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Drawer
      opened={item !== null}
      onClose={onClose}
      position="right"
      size="lg"
      title={t('system.logs.details')}
    >
      {item && <LogDetails item={item} />}
    </Drawer>
  );
}

function LogDetails({ item }: { item: SystemLogItem }) {
  const { t } = useTranslation();
  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Badge color={logLevelColors[item.level] ?? 'gray'}>{item.level}</Badge>
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
          <Code block>{prettyData(item.data)}</Code>
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
