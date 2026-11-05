import dayjs from 'dayjs';
import { IconAlertTriangle, IconCheck, IconCloudDownload, IconRefresh } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Card, Group, Stack, Text, Title, Tooltip } from '@mantine/core';
import type { SystemInfo } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { openUpdateDialog, openUpdateNotes, useUpdateState } from './update/updateStore';
import { useUpdateStatus } from './update/useUpdateStatus';

/**
 * docs/11 §2 System: the development line only moves forward. The card shows the newest release, the result of the
 * last update and the entry to the one update dialog; there is no channel switch and no rollback.
 */
export function UpdatesCard({ system }: { system: SystemInfo }) {
  const { t } = useTranslation();
  const { version, startedAt, lastUpdate } = system;
  const { check, status } = useUpdateStatus(system);
  const update = useUpdateState();
  const installed = !!system.installRoot;
  const latest = status?.latest;
  const applying = update.phase === 'preparing' || update.phase === 'restarting';
  const canShowNotes =
    update.phase === 'succeeded' && !!update.target?.notes && update.target.to === lastUpdate?.to;

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
          <Group gap="xs" wrap="wrap">
            <Text size="sm" c={lastUpdate.outcome === 'succeeded' ? 'dimmed' : 'red'}>
              {t('system.lastResult')}: {lastUpdate.message} ({dayjs(lastUpdate.at).format('LLL')})
            </Text>
            {canShowNotes && (
              <Button size="compact-xs" variant="subtle" onClick={openUpdateNotes}>
                {t('system.whatsNew')}
              </Button>
            )}
          </Group>
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

        {applying && (
          <Alert color="violet" icon={<IconRefresh aria-hidden />}>
            <Text size="sm">
              {t('system.updating', { version: update.target?.to ?? latest ?? '' })}
            </Text>
          </Alert>
        )}

        {!applying && status && !check.isError && (
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
                <Button size="xs" disabled={!installed} onClick={openUpdateDialog}>
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
