import { useTranslation } from 'react-i18next';
import { NumberInput, SimpleGrid, Switch, TextInput } from '@mantine/core';
import type { NotificationDestinationUpdate } from '@/api/generated';
import { HelpField } from '@/components/help/HelpLabel';

/** How a destination delivers: mention, panel link, timing and the on/off switch. */
export function DestinationDeliveryFields({
  value,
  set,
}: {
  value: NotificationDestinationUpdate;
  set: <K extends keyof NotificationDestinationUpdate>(
    key: K,
    next: NotificationDestinationUpdate[K]
  ) => void;
}) {
  const { t } = useTranslation();

  return (
    <>
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        <HelpField
          label={t('system.notifications.mentionRole')}
          help={{ topicId: 'notifications.mentionRole' }}
        >
          {(id) => (
            <TextInput
              id={id}
              description={t('system.notifications.mentionRoleHint')}
              value={value.mentionRoleId ?? ''}
              onChange={(event) => set('mentionRoleId', event.currentTarget.value || null)}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.panelUrl')}
          help={{ topicId: 'notifications.panelUrl' }}
        >
          {(id) => (
            <TextInput
              id={id}
              placeholder="http://100.96.2.89:8765"
              value={value.panelUrl ?? ''}
              onChange={(event) => set('panelUrl', event.currentTarget.value || null)}
            />
          )}
        </HelpField>
      </SimpleGrid>
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        <HelpField
          label={t('system.notifications.intervalSec')}
          help={{ topicId: 'notifications.timing' }}
        >
          {(id) => (
            <NumberInput
              id={id}
              min={10}
              max={3600}
              value={value.intervalSec}
              onChange={(next) => set('intervalSec', Number(next) || 60)}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.digestSec')}
          help={{ topicId: 'notifications.timing' }}
        >
          {(id) => (
            <NumberInput
              id={id}
              min={30}
              max={3600}
              value={value.digestSec}
              onChange={(next) => set('digestSec', Number(next) || 300)}
            />
          )}
        </HelpField>
      </SimpleGrid>
      <HelpField
        label={t('system.notifications.enabled')}
        help={{ topicId: 'notifications.enabled' }}
      >
        {(id) => (
          <Switch
            id={id}
            checked={value.enabled}
            onChange={(event) => set('enabled', event.currentTarget.checked)}
          />
        )}
      </HelpField>
    </>
  );
}
