import dayjs from 'dayjs';
import type { ReactNode } from 'react';
import {
  IconAlertTriangle,
  IconArrowRight,
  IconCheck,
  IconCloudDownload,
  IconCopy,
  IconRefresh,
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Accordion,
  Alert,
  Anchor,
  Badge,
  Button,
  Group,
  Loader,
  Modal,
  Skeleton,
  Stack,
  Text,
  ThemeIcon,
  useMantineTheme,
} from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { AnchorLink } from '@/components/links';
import { ReleaseNotes } from './ReleaseNotes';
import { closeUpdateDialog, openUpdateNotes, useUpdateState } from './updateStore';
import { useStartUpdate } from './useStartUpdate';
import { useUpdateStatus } from './useUpdateStatus';

/**
 * One place to review and start an update (docs/11 §2 System): what changes, what is ready, the notes and the data
 * warning. The progress and result live in UpdateBanner, so the dialog can close as soon as the update starts.
 */
export function UpdateDialog() {
  const { t } = useTranslation();
  const theme = useMantineTheme();
  const phone = useMediaQuery(`(max-width: ${theme.breakpoints.sm})`);
  const system = useQuery(queries.system());
  const jobs = useQuery(queries.jobs());
  const { status, check } = useUpdateStatus(system.data);
  const state = useUpdateState();
  const { start, pending } = useStartUpdate();

  const latest = status?.latest ?? null;
  const notes = status?.notes?.trim() ?? '';
  const installed = !!system.data?.installRoot;
  const runningJobs = (jobs.data ?? []).filter(
    (job) => job.state === 'running' || job.state === 'queued'
  );
  const checking = check.isFetching;
  const blocked = !installed || runningJobs.length > 0 || !status?.available || !latest;
  const dataRoot = system.data?.dataRoot;
  const diskFreeGb =
    system.data?.machine.diskFreeMb != null
      ? Math.round(system.data.machine.diskFreeMb / 1024)
      : null;

  const reason = !system.data
    ? t('system.checking')
    : !status
      ? t('system.checking')
      : !status.available
        ? t('system.upToDate')
        : !latest
          ? t('system.checkFailed')
          : null;

  const copyBackups = () => {
    if (!dataRoot) {
      return;
    }

    void navigator.clipboard
      .writeText(`${dataRoot}\\instances\n${dataRoot}\\db`)
      .then(() => notifications.show({ color: 'green', message: t('actions.copied') }))
      .catch(() =>
        notifications.show({
          color: 'red',
          title: t('feedback.failed'),
          message: t('system.copyBackupFailed'),
        })
      );
  };

  const confirm = () => {
    if (!latest || !system.data || blocked) {
      return;
    }

    start({
      from: system.data.version,
      to: latest,
      notes: notes || null,
      url: status?.url ?? null,
      publishedAt: status?.publishedAt ?? null,
    });
    closeUpdateDialog();
  };

  return (
    <Modal
      opened={state.dialogOpen}
      onClose={closeUpdateDialog}
      size="lg"
      fullScreen={phone}
      transitionProps={{ transition: 'fade' }}
      title={
        status?.available && latest
          ? t('system.updateTitle', { version: latest })
          : t('system.version')
      }
    >
      <Stack gap="md">
        {state.phase === 'failed' && state.error && (!latest || state.target?.to === latest) && (
          <Alert color="red" icon={<IconAlertTriangle aria-hidden />} title={t('feedback.failed')}>
            <Text size="sm">{state.error}</Text>
          </Alert>
        )}

        {!system.data ? (
          <Skeleton h={180} />
        ) : (
          <>
            <Group gap="xs" wrap="wrap">
              <Badge variant="light" color="gray" size="lg" tt="none">
                {system.data.version}
              </Badge>
              <IconArrowRight size={16} aria-hidden />
              <Badge variant="light" color="violet" size="lg" tt="none">
                {latest ?? '—'}
              </Badge>
              {status?.publishedAt && (
                <Text size="sm" c="dimmed">
                  {t('system.publishedAt', { time: dayjs(status.publishedAt).fromNow() })}
                </Text>
              )}
            </Group>

            <Stack gap={6}>
              <CheckRow ok={installed} warning={!installed}>
                {installed ? t('system.preflight.installed') : t('system.devBuild')}
              </CheckRow>
              <CheckRow ok={runningJobs.length === 0} warning>
                {runningJobs.length === 0
                  ? t('system.preflight.noJobs')
                  : t('system.updateBlockedJobs', { jobs: runningJobs.length })}
              </CheckRow>
              <CheckRow ok>{t('system.preflight.gamesKeepRunning')}</CheckRow>
              {diskFreeGb != null && (
                <Text size="sm" c="dimmed" pl={30}>
                  {t('system.preflight.diskFree', { size: diskFreeGb })}
                </Text>
              )}
            </Stack>

            {runningJobs.length > 0 && (
              <AnchorLink to="/system/jobs" size="sm" onClick={closeUpdateDialog}>
                {t('system.openJobs')}
              </AnchorLink>
            )}

            {check.isError && (
              <Alert
                color="red"
                icon={<IconAlertTriangle aria-hidden />}
                title={t('system.checkFailed')}
              >
                <Stack gap="xs" align="flex-start">
                  <Text size="sm">{toProblem(check.error, t('errors.generic')).title}</Text>
                  <Button
                    size="xs"
                    variant="light"
                    leftSection={<IconRefresh size={14} />}
                    onClick={() => void check.refetch()}
                  >
                    {t('actions.retry')}
                  </Button>
                </Stack>
              </Alert>
            )}

            {checking && !check.isError && (
              <Group gap="xs">
                <Loader size="xs" />
                <Text size="sm" c="dimmed">
                  {t('system.checking')}
                </Text>
              </Group>
            )}

            {notes ? (
              <Accordion variant="separated">
                <Accordion.Item value="notes">
                  <Accordion.Control>{t('system.releaseNotes')}</Accordion.Control>
                  <Accordion.Panel>
                    <ReleaseNotes notes={notes} />
                  </Accordion.Panel>
                </Accordion.Item>
              </Accordion>
            ) : (
              <Text size="sm" c="dimmed">
                {t('system.noNotes')}
              </Text>
            )}

            <Alert color="yellow" icon={<IconAlertTriangle aria-hidden />}>
              <Stack gap="xs" align="flex-start">
                <Text size="sm">
                  {dataRoot ? t('system.dataWarning', { dataRoot }) : t('system.dataWarningNoRoot')}
                </Text>
                {dataRoot && (
                  <Button
                    size="compact-xs"
                    variant="light"
                    leftSection={<IconCopy size={14} />}
                    onClick={copyBackups}
                  >
                    {t('system.copyBackupPaths')}
                  </Button>
                )}
              </Stack>
            </Alert>

            {status?.url && (
              <Anchor href={status.url} target="_blank" rel="noreferrer" size="sm">
                {t('system.openRelease')}
              </Anchor>
            )}

            {reason && (
              <Text size="sm" c="dimmed">
                {reason}
              </Text>
            )}
          </>
        )}

        <Group justify="flex-end">
          <Button variant="default" onClick={closeUpdateDialog} data-autofocus>
            {t('actions.cancel')}
          </Button>
          {state.phase === 'succeeded' && state.target?.notes && (
            <Button variant="light" onClick={openUpdateNotes}>
              {t('system.whatsNew')}
            </Button>
          )}
          <Button
            leftSection={<IconCloudDownload size={16} />}
            loading={pending}
            disabled={blocked}
            onClick={confirm}
          >
            {latest ? t('system.update', { version: latest }) : t('system.updateShort')}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function CheckRow({
  ok,
  warning = false,
  children,
}: {
  ok: boolean;
  warning?: boolean;
  children: ReactNode;
}) {
  return (
    <Group gap="xs" wrap="nowrap">
      <ThemeIcon
        size={20}
        radius="xl"
        variant="light"
        color={ok ? 'green' : warning ? 'yellow' : 'gray'}
      >
        {ok ? <IconCheck size={12} aria-hidden /> : <IconAlertTriangle size={12} aria-hidden />}
      </ThemeIcon>
      <Text size="sm">{children}</Text>
    </Group>
  );
}
