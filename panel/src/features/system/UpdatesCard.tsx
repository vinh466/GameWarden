import dayjs from 'dayjs';
import { IconAlertTriangle, IconCheck, IconCloudDownload, IconRefresh } from '@tabler/icons-react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Anchor,
  Button,
  Card,
  Group,
  ScrollArea,
  Stack,
  Text,
  Title,
  Tooltip,
} from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { startUpdate, type SystemInfo } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';

/**
 * docs/11 §2 System: the development line only moves forward. The card shows the newest release, its notes (where
 * the backup/reset instructions live), and the result of the last update. There is no channel switch and no rollback.
 */
export function UpdatesCard({ system }: { system: SystemInfo }) {
  const { t } = useTranslation();
  const { version, startedAt, lastUpdate } = system;
  const installed = !!system.installRoot;
  const check = useQuery(queries.updateCheck());
  const status = check.data ?? system.updateCheck;
  const latest = status?.latest;
  const notes = status?.notes?.trim();
  const apply = useMutation({
    mutationFn: () => startUpdate({ body: {}, throwOnError: true }),
    onSuccess: ({ data }) =>
      notifications.show({
        color: 'violet',
        loading: true,
        message: t('system.updating', { version: data.to }),
        autoClose: 15_000,
      }),
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const confirmUpdate = () => {
    if (!latest) {
      return;
    }

    modals.openConfirmModal({
      title: t('system.updateTitle', { version: latest }),
      size: 'lg',
      children: (
        <Stack gap="sm">
          <Text size="sm">{t('system.updateText')}</Text>
          <Alert color="yellow" icon={<IconAlertTriangle aria-hidden />}>
            <Text size="sm">{t('system.dataWarning', { dataRoot: system.dataRoot })}</Text>
          </Alert>
          {notes ? (
            <Stack gap={4}>
              <Text size="sm" fw={600}>
                {t('system.releaseNotes')}
              </Text>
              <ScrollArea.Autosize mah={220} type="auto">
                <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
                  {notes}
                </Text>
              </ScrollArea.Autosize>
            </Stack>
          ) : (
            <Text size="sm" c="dimmed">
              {t('system.noNotes')}
            </Text>
          )}
          {status?.url && (
            <Anchor href={status.url} target="_blank" rel="noreferrer" size="sm">
              {t('system.openRelease')}
            </Anchor>
          )}
        </Stack>
      ),
      labels: {
        confirm: t('system.update', { version: latest }),
        cancel: t('actions.cancel'),
      },
      onConfirm: () => apply.mutate(),
    });
  };

  return (
    <Card>
      <Stack gap="xs">
        <Group justify="space-between" align="flex-start" gap="md">
          <Stack gap={4}>
            <Title order={5}>{t('system.version')}</Title>
            <Text size="sm" c="dimmed">
              {t('system.running', { version, since: dayjs(startedAt).fromNow() })}
            </Text>
          </Stack>
          <Tooltip label={t('system.checkHint')}>
            <Button
              variant="default"
              size="xs"
              leftSection={<IconRefresh size={14} />}
              loading={check.isFetching}
              onClick={() => void check.refetch()}
            >
              {t('system.check')}
            </Button>
          </Tooltip>
        </Group>

        <Group gap="xs" wrap="wrap">
          <Text size="sm">
            {t('system.latest')}: <strong>{latest ?? '—'}</strong>
          </Text>
          {status?.checkedAt && (
            <Text size="sm" c="dimmed">
              {t('system.checked', { time: dayjs(status.checkedAt).fromNow() })}
            </Text>
          )}
        </Group>

        {!installed && (
          <Text size="sm" c="dimmed">
            {t('system.devBuild')}
          </Text>
        )}

        {lastUpdate && (
          <Text size="sm" c={lastUpdate.outcome === 'succeeded' ? 'dimmed' : 'red'}>
            {t('system.lastResult')}: {lastUpdate.message} ({dayjs(lastUpdate.at).format('LLL')})
          </Text>
        )}

        {check.isError && (
          <Alert
            color="red"
            icon={<IconAlertTriangle aria-hidden />}
            title={t('system.checkFailed')}
          >
            <Stack gap="xs">
              <Text size="sm">{toProblem(check.error, t('errors.generic')).title}</Text>
              <Button size="xs" variant="light" onClick={() => void check.refetch()}>
                {t('actions.retry')}
              </Button>
            </Stack>
          </Alert>
        )}

        {status && !check.isError && (
          <Alert
            color={status.available ? 'violet' : 'green'}
            icon={status.available ? <IconCloudDownload aria-hidden /> : <IconCheck aria-hidden />}
          >
            <Group justify="space-between" gap="xs">
              <Text size="sm">
                {status.available && latest
                  ? t('system.available', { version: latest })
                  : t('system.upToDate')}
              </Text>
              {status.available && latest && (
                <Button
                  size="xs"
                  loading={apply.isPending}
                  disabled={!installed}
                  onClick={confirmUpdate}
                >
                  {t('system.update', { version: latest })}
                </Button>
              )}
            </Group>
          </Alert>
        )}
      </Stack>
    </Card>
  );
}
