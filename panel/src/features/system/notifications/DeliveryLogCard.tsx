import dayjs from 'dayjs';
import { IconRefresh, IconSend } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Card, Group, Skeleton, Table, Text, Title } from '@mantine/core';
import type { NotificationDeliveryView } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';
import { HelpButton } from '@/components/help/HelpButton';
import { ProblemAlert } from '@/components/ProblemAlert';

/** The delivery log: what Core sent, what failed and the retry for a failed attempt. */
export function DeliveryLogCard({
  deliveries,
  error,
  loading,
  retryingId,
  onRetry,
  onRefresh,
}: {
  deliveries?: NotificationDeliveryView[];
  error: unknown;
  loading: boolean;
  retryingId?: number;
  onRetry: (delivery: NotificationDeliveryView) => void;
  onRefresh: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Group gap="xs">
          <Title order={5}>{t('system.notifications.deliveries')}</Title>
          <HelpButton
            label={t('system.notifications.deliveries')}
            help={{ topicId: 'notifications.deliveries' }}
          />
        </Group>
        <Button
          size="xs"
          variant="light"
          leftSection={<IconRefresh size={14} />}
          onClick={onRefresh}
          loading={loading}
        >
          {t('actions.refresh')}
        </Button>
      </Group>
      {error ? (
        <ProblemAlert error={error} onRetry={onRefresh} />
      ) : !deliveries ? (
        <Skeleton h={160} />
      ) : deliveries.length === 0 ? (
        <EmptyState icon={IconSend} title={t('system.notifications.noDeliveries')} />
      ) : (
        <Table.ScrollContainer minWidth={900}>
          <Table highlightOnHover aria-label={t('system.notifications.deliveries')}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('system.notifications.time')}</Table.Th>
                <Table.Th>{t('system.notifications.topic')}</Table.Th>
                <Table.Th>{t('system.notifications.destination')}</Table.Th>
                <Table.Th>{t('system.notifications.state')}</Table.Th>
                <Table.Th ta="right">{t('system.notifications.attempts')}</Table.Th>
                <Table.Th>{t('system.notifications.error')}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {deliveries.map((delivery) => (
                <Table.Tr key={delivery.id}>
                  <Table.Td>
                    <Text
                      component="time"
                      dateTime={delivery.at}
                      size="xs"
                      ff="monospace"
                      title={dayjs(delivery.at).format('LLL')}
                    >
                      {dayjs(delivery.at).format('HH:mm:ss')}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {dayjs(delivery.at).format('YYYY-MM-DD')}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs">{delivery.topic}</Text>
                    <Text size="xs" c="dimmed" lineClamp={1}>
                      {delivery.title}
                    </Text>
                  </Table.Td>
                  <Table.Td>{delivery.destinationId}</Table.Td>
                  <Table.Td>
                    <Badge color={deliveryColor(delivery.state)} variant="light">
                      {delivery.state}
                    </Badge>
                  </Table.Td>
                  <Table.Td ta="right">{delivery.attempts}</Table.Td>
                  <Table.Td>
                    <Text size="xs" c="red" lineClamp={2} maw={260}>
                      {delivery.lastError}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    {delivery.state !== 'sent' && (
                      <Button
                        size="compact-xs"
                        variant="light"
                        onClick={() => onRetry(delivery)}
                        loading={retryingId === delivery.id}
                      >
                        {t('actions.retry')}
                      </Button>
                    )}
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

function deliveryColor(state: string) {
  return state === 'sent'
    ? 'green'
    : state === 'failed'
      ? 'red'
      : state === 'dropped'
        ? 'gray'
        : 'yellow';
}
