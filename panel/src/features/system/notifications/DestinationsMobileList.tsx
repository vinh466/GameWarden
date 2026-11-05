import { useTranslation } from 'react-i18next';
import { Badge, Divider, Group, Stack, Text } from '@mantine/core';
import type { NotificationDeliveryView, NotificationDestinationView } from '@/api/generated';
import { DestinationActions } from './DestinationActions';

/** Destinations as readable cards on phones; the table's action column would sit off-screen (docs/11 §6.7). */
export function DestinationsMobileList({
  destinations,
  deliveries,
  busy,
  testingId,
  onEdit,
  onTest,
  onToggle,
  onDelete,
}: {
  destinations: NotificationDestinationView[];
  deliveries: NotificationDeliveryView[];
  busy: boolean;
  testingId?: string;
  onEdit: (destination: NotificationDestinationView) => void;
  onTest: (destination: NotificationDestinationView) => void;
  onToggle: (destination: NotificationDestinationView) => void;
  onDelete: (destination: NotificationDestinationView) => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap={0} hiddenFrom="sm">
      {destinations.map((destination, index) => {
        const last = deliveries.find((item) => item.destinationId === destination.id)?.state;
        return (
          <div key={destination.id}>
            <Stack gap={6} p="sm">
              <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
                <Stack gap={0} miw={0}>
                  <Text size="sm" fw={600}>
                    {destination.name}
                  </Text>
                  <Text size="xs" c="dimmed" ff="monospace">
                    {destination.id}
                  </Text>
                </Stack>
                <Badge color={destination.enabled ? 'green' : 'gray'} variant="light">
                  {destination.enabled
                    ? t('system.notifications.enabled')
                    : t('system.notifications.disabled')}
                </Badge>
              </Group>
              <Text size="xs" c="dimmed">
                {t('system.notifications.summary', {
                  severity: destination.minimumSeverity,
                  topics: destination.topics.length === 0 ? '*' : destination.topics.join(', '),
                })}
              </Text>
              <Group gap="xs">
                <Badge variant="outline">
                  {t(`system.notifications.modes.${destination.mode}`, {
                    defaultValue: destination.mode,
                  })}
                </Badge>
                <Text size="xs" c={destination.webhookSet ? 'green' : 'red'}>
                  {destination.channel === 'discord-bot'
                    ? t('system.notifications.channelBot')
                    : destination.webhookSet
                      ? t('system.notifications.webhookSet')
                      : t('system.notifications.webhookMissing')}
                </Text>
              </Group>
              <Text size="xs" c="dimmed">
                {last
                  ? t('system.notifications.lastDelivery', {
                      state: t(`system.notifications.stateValues.${last}`, { defaultValue: last }),
                    })
                  : t('system.notifications.neverDelivered')}
              </Text>
              <DestinationActions
                destination={destination}
                busy={busy}
                testing={testingId === destination.id}
                size="xs"
                justify="flex-start"
                onEdit={onEdit}
                onTest={onTest}
                onToggle={onToggle}
                onDelete={onDelete}
              />
            </Stack>
            {index < destinations.length - 1 && <Divider />}
          </div>
        );
      })}
    </Stack>
  );
}
