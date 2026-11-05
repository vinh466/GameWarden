import dayjs from 'dayjs';
import { useEffect, useState } from 'react';
import {
  IconAlertTriangle,
  IconCheck,
  IconCloudDownload,
  IconCopy,
  IconRefresh,
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Code, Group, Progress, Stack, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { queries } from '@/api/queries';
import {
  dismissUpdateBanner,
  hasReloadedInto,
  isUpdateActive,
  isUpdateBannerVisible,
  markFailed,
  markReloadedInto,
  markRestarting,
  openUpdateDialog,
  openUpdateNotes,
  useUpdateState,
} from './updateStore';

/** 10 §6: Setup waits up to 120 s for the new version; the panel warns a little later. */
const StalledAfterMs = 150_000;

/**
 * The global progress surface of an update (docs/11 §2 System, §6.1). It replaces the red "lost connection" banner
 * while Core restarts, so an update never looks like a failure, and it survives a reload through updateStore.
 */
export function UpdateBanner() {
  const { t } = useTranslation();
  const state = useUpdateState();
  const active = isUpdateActive(state);
  const now = useNow(active);
  const progress = useQuery({
    ...queries.updateStatus(),
    enabled: state.phase === 'preparing',
  });

  useEffect(() => {
    if (!isUpdateActive(state) || !progress.data) {
      return;
    }

    const statusStartedAt = progress.data.startedAt ? Date.parse(progress.data.startedAt) : null;
    const currentAttempt =
      statusStartedAt != null && (!state.startedAt || statusStartedAt >= state.startedAt - 5_000);
    if (!currentAttempt) {
      return;
    }

    if (progress.data.phase === 'restarting') {
      markRestarting();
    } else if (progress.data.phase === 'failed') {
      markFailed(progress.data.error ?? progress.data.message);
    }
  }, [progress.data, state]);

  if (!isUpdateBannerVisible(state) || !state.target) {
    return null;
  }

  const target = state.target;
  const step = progress.data?.phase;
  const percent = progress.data?.percent ?? null;
  const stalled = active && state.phaseAt != null && now - state.phaseAt > StalledAfterMs;
  const elapsed = state.startedAt != null ? dayjs(state.startedAt).fromNow(true) : null;
  const reloaded = hasReloadedInto(target.to);

  if (state.phase === 'preparing') {
    return (
      <Alert
        color="violet"
        icon={<IconCloudDownload aria-hidden />}
        title={t('system.updateBanner.preparingTitle', { version: target.to })}
        role="status"
        mb="md"
      >
        <Stack gap="xs">
          <Group justify="space-between" gap="xs" wrap="nowrap">
            <Text size="sm">
              {step === 'verifying'
                ? t('system.updateBanner.verifying')
                : step === 'extracting'
                  ? t('system.updateBanner.extracting')
                  : t('system.updateBanner.downloading')}
            </Text>
            {percent != null && (
              <Text size="xs" fw={600} ff="monospace">
                {Math.round(percent)}%
              </Text>
            )}
          </Group>
          <Progress
            value={percent ?? 100}
            striped={percent == null}
            animated
            size="sm"
            aria-label={t('system.updateBanner.progressLabel')}
          />
          {stalled && (
            <Group gap="xs" c="orange">
              <IconAlertTriangle size={16} aria-hidden />
              <Text size="sm">{t('system.updateBanner.preparingSlow')}</Text>
            </Group>
          )}
          {elapsed && (
            <Text size="xs" c="dimmed">
              {t('system.updateBanner.elapsed', { time: elapsed })}
            </Text>
          )}
        </Stack>
      </Alert>
    );
  }

  if (state.phase === 'restarting') {
    return (
      <Alert
        color="violet"
        icon={<IconRefresh aria-hidden />}
        title={t('system.updateBanner.restartingTitle', { version: target.to })}
        role="status"
        mb="md"
      >
        <Stack gap="xs">
          <Text size="sm">{t('system.updateBanner.restartingText')}</Text>
          {stalled ? (
            <Stack gap="xs">
              <Group gap="xs" c="orange">
                <IconAlertTriangle size={16} aria-hidden />
                <Text size="sm">{t('system.updateBanner.stalledText')}</Text>
              </Group>
              <Group gap="xs">
                <Code>gamewarden status</Code>
                <CopyStatus />
              </Group>
            </Stack>
          ) : (
            elapsed && (
              <Text size="xs" c="dimmed">
                {t('system.updateBanner.elapsed', { time: elapsed })}
              </Text>
            )
          )}
        </Stack>
      </Alert>
    );
  }

  if (state.phase === 'succeeded') {
    return (
      <Alert
        color="green"
        icon={<IconCheck aria-hidden />}
        title={t('system.updateBanner.succeededTitle', { version: target.to })}
        role="status"
        withCloseButton
        closeButtonLabel={t('actions.close')}
        onClose={dismissUpdateBanner}
        mb="md"
      >
        <Stack gap="xs">
          <Text size="sm">
            {reloaded
              ? t('system.updateBanner.succeededReloaded')
              : t('system.updateBanner.succeededText')}
          </Text>
          <Group gap="xs">
            {!reloaded && (
              <Button
                size="xs"
                leftSection={<IconRefresh size={14} />}
                onClick={() => {
                  markReloadedInto(target.to);
                  window.location.reload();
                }}
              >
                {t('system.updateBanner.reload')}
              </Button>
            )}
            {target.notes && (
              <Button size="xs" variant="light" onClick={openUpdateNotes}>
                {t('system.whatsNew')}
              </Button>
            )}
          </Group>
        </Stack>
      </Alert>
    );
  }

  return (
    <Alert
      color="red"
      icon={<IconAlertTriangle aria-hidden />}
      title={t('system.updateBanner.failedTitle', { version: target.to })}
      role="alert"
      withCloseButton
      closeButtonLabel={t('actions.close')}
      onClose={dismissUpdateBanner}
      mb="md"
    >
      <Stack gap="xs">
        <Text size="sm">{state.error ?? t('system.updateBanner.failedText')}</Text>
        <Text size="sm" c="dimmed">
          {t('system.updateBanner.failedStill', { version: target.from })}
        </Text>
        <Group gap="xs">
          <Button size="xs" variant="light" onClick={openUpdateDialog}>
            {t('actions.retry')}
          </Button>
        </Group>
      </Stack>
    </Alert>
  );
}

function CopyStatus() {
  const { t } = useTranslation();
  const copy = () => {
    void navigator.clipboard
      .writeText('gamewarden status')
      .then(() => notifications.show({ color: 'green', message: t('actions.copied') }))
      .catch(() =>
        notifications.show({
          color: 'red',
          title: t('feedback.failed'),
          message: t('system.updateBanner.copyFailed'),
        })
      );
  };

  return (
    <Button size="compact-xs" variant="light" leftSection={<IconCopy size={14} />} onClick={copy}>
      {t('actions.copy')}
    </Button>
  );
}

function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) {
      return;
    }

    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 1_000);
    return () => window.clearInterval(timer);
  }, [active]);
  return now;
}
