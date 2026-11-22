import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Divider,
  Group,
  PasswordInput,
  Select,
  SimpleGrid,
  Stack,
  TextInput,
} from '@mantine/core';
import type { NotificationDestinationUpdate, NotificationSettingsView } from '@/api/generated';
import { FormFooter } from '@/components/FormSections';
import { HelpField } from '@/components/help/HelpLabel';
import { DestinationDeliveryFields } from './DestinationDeliveryFields';
import { DestinationRoutingFields } from './DestinationRoutingFields';
import { DestinationTargetFields } from './DestinationTargetFields';

/** Add or edit a destination; the webhook is write-only and never prefilled (docs/18). */
export function DestinationForm({
  destination,
  modes,
  catalog,
  instances,
  editing,
  busy,
  onSave,
  onCancel,
}: {
  destination: NotificationDestinationUpdate;
  modes: string[];
  catalog: NotificationSettingsView['catalog'];
  instances: { id: string; label: string; state: string }[];
  editing: boolean;
  busy: boolean;
  onSave: (destination: NotificationDestinationUpdate) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const [value, setValue] = useState(destination);
  useEffect(() => setValue(destination), [destination]);

  const set = <K extends keyof NotificationDestinationUpdate>(
    key: K,
    next: NotificationDestinationUpdate[K]
  ) => setValue((current) => ({ ...current, [key]: next }));

  return (
    <Stack gap="sm">
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        <HelpField
          label={t('system.notifications.id')}
          help={{ topicId: 'notifications.identity' }}
          required
        >
          {(id) => (
            <TextInput
              id={id}
              description={t('system.notifications.idHint')}
              disabled={editing}
              value={value.id}
              onChange={(event) => set('id', event.currentTarget.value)}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.name')}
          help={{ topicId: 'notifications.identity' }}
          required
        >
          {(id) => (
            <TextInput
              id={id}
              value={value.name}
              onChange={(event) => set('name', event.currentTarget.value)}
            />
          )}
        </HelpField>
      </SimpleGrid>
      <HelpField
        label={t('system.notifications.channel')}
        help={{ topicId: 'notifications.channel' }}
      >
        {(id) => (
          <Select
            id={id}
            data={[
              {
                value: 'discord-webhook',
                label: t('system.notifications.channelWebhook'),
              },
              {
                value: 'discord-bot',
                label: t('system.notifications.channelBot'),
              },
            ]}
            value={value.channel}
            onChange={(next) => set('channel', next ?? 'discord-webhook')}
            allowDeselect={false}
            description={t('system.notifications.channelHint')}
          />
        )}
      </HelpField>
      {value.channel === 'discord-webhook' && (
        <HelpField
          label={t('system.notifications.webhook')}
          help={{ topicId: 'notifications.webhook' }}
          required={!editing}
        >
          {(id) => (
            <PasswordInput
              id={id}
              description={t('system.notifications.webhookHint')}
              placeholder={
                editing
                  ? t('system.notifications.webhookKeep')
                  : 'https://discord.com/api/webhooks/...'
              }
              value={value.webhookUrl ?? ''}
              onChange={(event) => set('webhookUrl', event.currentTarget.value || null)}
            />
          )}
        </HelpField>
      )}
      <DestinationRoutingFields value={value} set={set} modes={modes} />
      <DestinationTargetFields value={value} set={set} catalog={catalog} instances={instances} />
      <DestinationDeliveryFields value={value} set={set} />
      <Divider />
      <FormFooter>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onCancel}>
            {t('actions.cancel')}
          </Button>
          <Button
            loading={busy}
            disabled={
              !value.id ||
              !value.name ||
              (value.channel === 'discord-webhook' && !editing && !value.webhookUrl)
            }
            onClick={() => onSave(value)}
          >
            {t('actions.save')}
          </Button>
        </Group>
      </FormFooter>
    </Stack>
  );
}
