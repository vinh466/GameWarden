import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Divider, Group, Stack, Text } from '@mantine/core';
import type { NotificationDeliveryView } from '@/api/generated';

/** The delivery log as cards on phones, including the retry action (docs/11 §6.7). */
export function DeliveryLogMobileList({
  deliveries,
  retryingId,
  onRetry,
}: {
  deliveries: NotificationDeliveryView[];
  retryingId?: number;
  onRetry: (delivery: NotificationDeliveryView) => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap={0} hiddenFrom="sm">
      {deliveries.map((delivery, index) => (
        <div key={delivery.id}>
          <Stack gap={6} p="sm">
            <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
              <Stack gap={0} miw={0}>
                <Text size="sm" fw={500}>
                  {delivery.topic}
                </Text>
                <Text size="xs" c="dimmed" lineClamp={2}>
                  {delivery.title}
                </Text>
              </Stack>
              <Badge color={deliveryColor(delivery.state)} variant="light">
                {t(`system.notifications.stateValues.${delivery.state}`, {
                  defaultValue: delivery.state,
                })}
              </Badge>
            </Group>
            <Text
              size="xs"
              c="dimmed"
              component="time"
              dateTime={delivery.at}
              title={dayjs(delivery.at).format('LLL')}
            >
              {dayjs(delivery.at).format('YYYY-MM-DD HH:mm:ss')} · {delivery.destinationId}
            </Text>
            <Text size="xs" c="dimmed">
              {t('system.notifications.attempts')}: {delivery.attempts}
            </Text>
            {delivery.lastError && (
              <Text size="xs" c="red" lineClamp={3}>
                {delivery.lastError}
              </Text>
            )}
            {delivery.state !== 'sent' && (
              <Button
                size="xs"
                variant="light"
                onClick={() => onRetry(delivery)}
                loading={retryingId === delivery.id}
              >
                {t('actions.retry')}
              </Button>
            )}
          </Stack>
          {index < deliveries.length - 1 && <Divider />}
        </div>
      ))}
    </Stack>
  );
}

export function deliveryColor(state: string) {
  return state === 'sent'
    ? 'green'
    : state === 'failed'
      ? 'red'
      : state === 'dropped'
        ? 'gray'
        : 'yellow';
}
