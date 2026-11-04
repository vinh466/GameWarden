import { IconDots, IconEdit, IconPower, IconSend, IconTrash } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Button, Group, Menu } from '@mantine/core';
import type { NotificationDestinationView } from '@/api/generated';

/** Test, edit and the overflow menu for one destination; shared by the table and the phone cards. */
export function DestinationActions({
  destination,
  busy,
  testing,
  size = 'compact-xs',
  justify = 'flex-end',
  onEdit,
  onTest,
  onToggle,
  onDelete,
}: {
  destination: NotificationDestinationView;
  busy: boolean;
  testing: boolean;
  size?: 'compact-xs' | 'xs';
  justify?: 'flex-end' | 'flex-start';
  onEdit: (destination: NotificationDestinationView) => void;
  onTest: (destination: NotificationDestinationView) => void;
  onToggle: (destination: NotificationDestinationView) => void;
  onDelete: (destination: NotificationDestinationView) => void;
}) {
  const { t } = useTranslation();
  return (
    <Group gap="xs" justify={justify} wrap="nowrap">
      <Button
        size={size}
        variant="light"
        leftSection={<IconSend size={14} />}
        loading={testing}
        disabled={!destination.webhookSet}
        onClick={() => onTest(destination)}
      >
        {t('system.notifications.test')}
      </Button>
      <Button
        size={size}
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
            aria-label={t('system.notifications.actions', { name: destination.name })}
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
  );
}
