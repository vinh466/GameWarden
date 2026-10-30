import { IconAlertTriangle, IconInfoCircle } from '@tabler/icons-react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Card, Group, Stack, Switch, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { setInstanceModsEnabled, type InstanceSnapshot, type ModsView } from '@/api/generated';
import { queries } from '@/api/queries';
import { HelpButton } from '@/components/help/HelpButton';
import { useServerCommands } from '../useServerCommands';
import { refreshMods, useModsFailure } from './useModsSync';

/**
 * *Mods on* (docs/13 §4.3) with its help, and what it means now: *Restart to apply* while the server runs other mods
 * than its next start would load, a warning while Mods are on without a ready set.
 */
export function ModsOnCard({ server, mods }: { server: InstanceSnapshot; mods: ModsView }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const failed = useModsFailure();
  const { run, pending } = useServerCommands();
  const toggle = useMutation({
    mutationFn: async (enabled: boolean) =>
      (
        await setInstanceModsEnabled({
          path: { id: server.id },
          body: { enabled },
          throwOnError: true,
        })
      ).data,
    onSuccess: (view) => {
      queryClient.setQueryData(queries.mods(server.id).queryKey, view);
      notifications.show({
        color: 'violet',
        message: t(view.enabled ? 'mods.turnedOn' : 'mods.turnedOff', {
          server: server.displayName,
        }),
      });
    },
    onError: (error) => failed(server.displayName, error),
    onSettled: () => refreshMods(queryClient, server.id),
  });
  const enabled = toggle.isPending ? toggle.variables : mods.enabled;

  return (
    <Stack gap="sm">
      <Card>
        <Group justify="space-between" wrap="nowrap" align="flex-start" gap="md">
          <Stack gap={2}>
            <Group gap={4} wrap="nowrap">
              <Text fw={600}>{t('mods.on')}</Text>
              <HelpButton label={t('mods.on')} help={{ topicId: 'mods.on' }} />
            </Group>
            <Text size="sm" c="dimmed">
              {t('mods.onHint')}
            </Text>
          </Stack>
          <Switch
            size="md"
            aria-label={t('mods.on')}
            checked={enabled}
            disabled={toggle.isPending}
            onChange={(event) => toggle.mutate(event.currentTarget.checked)}
          />
        </Group>
      </Card>
      {mods.restartRequired && (
        <Alert color="blue" icon={<IconInfoCircle aria-hidden />}>
          <Group justify="space-between" gap="xs">
            <Text size="sm">{t('mods.restartRequired')}</Text>
            <Button
              size="xs"
              variant="light"
              loading={pending(server) === 'restart'}
              onClick={() => run(server, 'restart')}
            >
              {t('settings.restartNow')}
            </Button>
          </Group>
        </Alert>
      )}
      {mods.enabled && mods.active?.state !== 'ready' && (
        <Alert color="yellow" icon={<IconAlertTriangle aria-hidden />}>
          <Text size="sm">{t('mods.onWithoutSet')}</Text>
        </Alert>
      )}
    </Stack>
  );
}
