import dayjs from 'dayjs';
import { IconAlertTriangle, IconInfoCircle } from '@tabler/icons-react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Badge, Button, Card, Group, Stack, Switch, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { setInstanceModsEnabled, type InstanceSnapshot, type ModsView } from '@/api/generated';
import { queries } from '@/api/queries';
import { HelpButton } from '@/components/help/HelpButton';
import { useServerCommands } from '../useServerCommands';
import { refreshMods, useModsFailure } from './useModsSync';

/**
 * The Mods screen's status strip (docs/13 §4.10): Mods on, the set in use, how many mods are on, newer versions and
 * whether a restart would load something else. The actions live in the toolbar and the table below.
 */
export function ModsHeader({ server, mods }: { server: InstanceSnapshot; mods: ModsView }) {
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
  const entries = mods.active?.entries ?? [];
  const enabledCount = entries.filter((entry) => entry.enabled).length;
  const chosen = new Set(
    entries.filter((entry) => entry.origin === 'chosen').map((entry) => entry.package)
  );
  const updates = (mods.newerVersions?.updates ?? []).filter((update) =>
    chosen.has(update.package)
  ).length;

  return (
    <Card p="md">
      <Stack gap="sm">
        <Group justify="space-between" wrap="nowrap" align="flex-start" gap="md">
          <Stack gap={4}>
            <Group gap="xs" wrap="wrap">
              <Group gap={4} wrap="nowrap">
                <Text fw={700} size="lg">
                  {t('mods.title')}
                </Text>
                <HelpButton label={t('mods.on')} help={{ topicId: 'mods.on' }} />
              </Group>
              {mods.active && (
                <Badge variant="light" color="gray">
                  {t('mods.summary.set', { id: mods.active.id })}
                </Badge>
              )}
              {mods.active && (
                <Badge variant="default">{t('mods.summary.mods', { count: entries.length })}</Badge>
              )}
              <Badge variant="default" color={enabledCount > 0 ? 'green' : 'gray'}>
                {t('mods.summary.enabled', { count: enabledCount })}
              </Badge>
              {updates > 0 && (
                <Badge variant="light" color="violet">
                  {t('mods.summary.updates', { count: updates })}
                </Badge>
              )}
              {mods.runningSetId != null && mods.runningSetId !== mods.active?.id && (
                <Badge variant="outline" color="blue">
                  {t('mods.summary.running', { id: mods.runningSetId })}
                </Badge>
              )}
            </Group>
            {mods.active && (
              <Text size="sm" c="dimmed">
                {t('mods.setMeta', {
                  id: mods.active.id,
                  count: entries.length,
                  time: dayjs(mods.active.createdAt).fromNow(),
                })}
                {mods.active.origin === 'code' || mods.active.origin === 'edit'
                  ? ` · ${t(`mods.origin.${mods.active.origin}`)}`
                  : ''}
              </Text>
            )}
          </Stack>
          <Switch
            size="md"
            aria-label={t('mods.on')}
            checked={enabled}
            disabled={toggle.isPending}
            onChange={(event) => toggle.mutate(event.currentTarget.checked)}
          />
        </Group>

        {mods.restartRequired && (
          <Alert color="blue" icon={<IconInfoCircle aria-hidden />}>
            <Group justify="space-between" gap="xs" wrap="wrap">
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
    </Card>
  );
}
