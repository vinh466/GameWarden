import { useState } from 'react';
import { IconDeviceFloppy, IconSpeakerphone, IconUserCheck } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Button, Card, Divider, Group, Stack, Text, TextInput, Title } from '@mantine/core';
import type { GameIntegrationView } from '@/api/generated';
import { useIntegrationAction } from './useIntegrationAction';

/** The write actions a provider offers (docs/16), shown only when its capability list names them. */
export function IntegrationActions({
  id,
  server,
  view,
}: {
  id: string;
  server: string;
  view: GameIntegrationView;
}) {
  const { t } = useTranslation();
  const { run, pendingAction } = useIntegrationAction(id, server);
  const [message, setMessage] = useState('');
  const [userId, setUserId] = useState('');
  const can = (capability: string) => view.capabilities.includes(capability);
  if (!can('announce') && !can('save') && !can('unban')) {
    return null;
  }

  return (
    <Card>
      <Stack gap="sm">
        <Stack gap={2}>
          <Title order={5}>{t('integration.actions.title')}</Title>
          <Text size="sm" c="dimmed">
            {t('integration.actions.hint')}
          </Text>
        </Stack>

        {can('announce') && (
          <Group align="flex-end" wrap="nowrap">
            <TextInput
              label={t('integration.actions.announce')}
              placeholder={t('integration.actions.announcePlaceholder')}
              value={message}
              onChange={(event) => setMessage(event.currentTarget.value)}
              maxLength={512}
              flex={1}
            />
            <Button
              leftSection={<IconSpeakerphone size={16} />}
              loading={pendingAction === 'announce'}
              disabled={message.trim() === ''}
              onClick={() =>
                run(
                  { action: 'announce', message: message.trim() },
                  { onSuccess: () => setMessage('') }
                )
              }
            >
              {t('integration.actions.send')}
            </Button>
          </Group>
        )}

        {can('save') && (
          <>
            {can('announce') && <Divider />}
            <Group justify="space-between" align="center" gap="sm">
              <Stack gap={0}>
                <Text size="sm" fw={500}>
                  {t('integration.actions.save')}
                </Text>
                <Text size="xs" c="dimmed">
                  {t('integration.actions.saveHint')}
                </Text>
              </Stack>
              <Button
                variant="light"
                leftSection={<IconDeviceFloppy size={16} />}
                loading={pendingAction === 'save'}
                onClick={() => run({ action: 'save' })}
              >
                {t('integration.actions.save')}
              </Button>
            </Group>
          </>
        )}

        {can('unban') && (
          <>
            {(can('announce') || can('save')) && <Divider />}
            <Group align="flex-end" wrap="nowrap">
              <TextInput
                label={t('integration.actions.unban')}
                placeholder={t('integration.actions.unbanPlaceholder')}
                value={userId}
                onChange={(event) => setUserId(event.currentTarget.value)}
                ff="monospace"
                flex={1}
              />
              <Button
                variant="light"
                leftSection={<IconUserCheck size={16} />}
                loading={pendingAction === 'unban'}
                disabled={userId.trim() === ''}
                onClick={() =>
                  run(
                    { action: 'unban', userId: userId.trim() },
                    { onSuccess: () => setUserId('') }
                  )
                }
              >
                {t('integration.actions.unbanButton')}
              </Button>
            </Group>
          </>
        )}
      </Stack>
    </Card>
  );
}
