import {
  IconBell,
  IconDots,
  IconEdit,
  IconPlus,
  IconPower,
  IconSend,
  IconTrash,
} from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Badge, Button, Card, Group, Menu, Table, Text, Title } from '@mantine/core';
import type { NotificationDeliveryView, NotificationDestinationView } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';

/** The Discord destinations, their scope and the last delivery, with one place for their actions. */
export function DestinationsCard({
  destinations,
  deliveries,
  busy,
  testingId,
  onAdd,
  onEdit,
  onTest,
  onToggle,
  onDelete,
}: {
  destinations: NotificationDestinationView[];
  deliveries: NotificationDeliveryView[];
  busy: boolean;
  testingId?: string;
  onAdd: () => void;
  onEdit: (destination: NotificationDestinationView) => void;
  onTest: (destination: NotificationDestinationView) => void;
  onToggle: (destination: NotificationDestinationView) => void;
  onDelete: (destination: NotificationDestinationView) => void;
}) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" mb="md">
        <Title order={5}>{t('system.notifications.destinations')}</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={onAdd}>
          {t('system.notifications.add')}
        </Button>
      </Group>
      {destinations.length === 0 ? (
        <EmptyState
          icon={IconBell}
          title={t('system.notifications.empty')}
          text={t('system.notifications.emptyText')}
          action={
            <Button variant="default" leftSection={<IconPlus size={16} />} onClick={onAdd}>
              {t('system.notifications.add')}
            </Button>
          }
        />
      ) : (
        <Table.ScrollContainer minWidth={960}>
          <Table highlightOnHover aria-label={t('system.notifications.destinations')}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('system.notifications.name')}</Table.Th>
                <Table.Th>{t('system.notifications.state')}</Table.Th>
                <Table.Th>{t('system.notifications.mode')}</Table.Th>
                <Table.Th>{t('system.notifications.webhookColumn')}</Table.Th>
                <Table.Th>{t('system.notifications.lastDeliveryColumn')}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {destinations.map((destination) => {
                const last = deliveries.find(
                  (item) => item.destinationId === destination.id
                )?.state;
                return (
                  <Table.Tr key={destination.id}>
                    <Table.Td>
                      <Text size="sm" fw={600}>
                        {destination.name}
                      </Text>
                      <Text size="xs" c="dimmed" ff="monospace">
                        {destination.id}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {t('system.notifications.summary', {
                          severity: destination.minimumSeverity,
                          topics:
                            destination.topics.length === 0 ? '*' : destination.topics.join(', '),
                        })}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Badge color={destination.enabled ? 'green' : 'gray'} variant="light">
                        {destination.enabled
                          ? t('system.notifications.enabled')
                          : t('system.notifications.disabled')}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Badge variant="outline">
                        {t(`system.notifications.modes.${destination.mode}`, {
                          defaultValue: destination.mode,
                        })}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c={destination.webhookSet ? 'green' : 'red'}>
                        {destination.webhookSet
                          ? t('system.notifications.webhookSet')
                          : t('system.notifications.webhookMissing')}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">
                        {last ?? t('system.notifications.neverDelivered')}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap="xs" justify="flex-end" wrap="nowrap">
                        <Button
                          size="compact-xs"
                          variant="light"
                          leftSection={<IconSend size={14} />}
                          loading={testingId === destination.id}
                          disabled={!destination.webhookSet}
                          onClick={() => onTest(destination)}
                        >
                          {t('system.notifications.test')}
                        </Button>
                        <Button
                          size="compact-xs"
                          variant="default"
                          leftSection={<IconEdit size={14} />}
                          onClick={() => onEdit(destination)}
                        >
                          {t('actions.edit')}
                        </Button>
                        <Menu position="bottom-end">
                          <Menu.Target>
                            <ActionIcon
                              variant="subtle"
                              aria-label={t('system.notifications.actions', {
                                name: destination.name,
                              })}
                            >
                              <IconDots size={16} />
                            </ActionIcon>
                          </Menu.Target>
                          <Menu.Dropdown>
                            <Menu.Item
                              leftSection={<IconPower size={14} />}
                              disabled={busy}
                              onClick={() => onToggle(destination)}
                            >
                              {destination.enabled
                                ? t('system.notifications.disableAction')
                                : t('system.notifications.enableAction')}
                            </Menu.Item>
                            <Menu.Item
                              color="red"
                              leftSection={<IconTrash size={14} />}
                              onClick={() => onDelete(destination)}
                            >
                              {t('actions.delete')}
                            </Menu.Item>
                          </Menu.Dropdown>
                        </Menu>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Card>
  );
}
