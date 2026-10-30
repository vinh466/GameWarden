import dayjs from 'dayjs';
import { useState, type ReactNode } from 'react';
import {
  IconArrowBackUp,
  IconFileImport,
  IconPlus,
  IconRefresh,
  IconShare,
  IconUpload,
} from '@tabler/icons-react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Group, Text, Tooltip } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { checkInstanceModsUpdates, rollbackInstanceMods, type ModsView } from '@/api/generated';
import { queries } from '@/api/queries';
import { AddModModal } from './AddModModal';
import { ImportCodeModal } from './ImportCodeModal';
import { chosenEntries, chosenUpdates, withUpdates } from './modEntries';
import { ShareCodeModal } from './ShareCodeModal';
import { refreshMods, useModsFailure, useModsSync } from './useModsSync';

/** A disabled action keeps its place and says why (docs/11 §6 rule 2). */
function Why({ reason, children }: { reason: string | null; children: ReactNode }) {
  return reason ? (
    <Tooltip label={reason} multiline maw={280}>
      <span>{children}</span>
    </Tooltip>
  ) : (
    children
  );
}

/**
 * The Mods tab's actions (docs/13 §4.10): import a code, add a mod, check for updates and update all, the share code
 * for friends, and back to the previous set, which asks first.
 */
export function ModActions({
  id,
  server,
  mods,
  busyReason,
}: {
  id: string;
  server: string;
  mods: ModsView;
  busyReason: string | null;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const failed = useModsFailure();
  const sync = useModsSync(id, server);
  const [open, setOpen] = useState<'import' | 'add' | 'share' | null>(null);
  const close = () => setOpen(null);
  const updates = chosenUpdates(mods);
  const hasMods = (mods.active?.entries ?? []).some((entry) => entry.enabled);

  const check = useMutation({
    mutationFn: async () =>
      (await checkInstanceModsUpdates({ path: { id }, throwOnError: true })).data,
    onSuccess: (result) => {
      const chosen = new Set(chosenEntries(mods.active).map((entry) => entry.package));
      const count = result.updates.length;
      notifications.show({
        color: 'violet',
        message:
          count === 0
            ? t('mods.upToDate')
            : result.updates.some((update) => chosen.has(update.package))
              ? t('mods.newerFound', { count })
              : t('mods.onlyDependencies'),
      });
    },
    onError: (error) => failed(server, error),
    onSettled: () => refreshMods(queryClient, id),
  });

  const rollback = useMutation({
    mutationFn: async () => (await rollbackInstanceMods({ path: { id }, throwOnError: true })).data,
    onSuccess: (view) => {
      queryClient.setQueryData(queries.mods(id).queryKey, view);
      notifications.show({ color: 'violet', message: t('mods.rollbackDone', { server }) });
    },
    onError: (error) => failed(server, error),
    onSettled: () => refreshMods(queryClient, id),
  });

  const confirmRollback = () =>
    modals.openConfirmModal({
      title: t('mods.rollbackTitle'),
      children: (
        <Text size="sm">
          {t('mods.rollbackText', {
            count: mods.previous?.entries.length ?? 0,
            time: dayjs(mods.previous?.createdAt).fromNow(),
          })}
        </Text>
      ),
      labels: { confirm: t('mods.rollbackConfirm'), cancel: t('actions.cancel') },
      onConfirm: () => rollback.mutate(),
    });

  const noActive = mods.active ? null : t('mods.noActive');
  return (
    <>
      <Group gap="xs">
        <Why reason={busyReason}>
          <Button
            leftSection={<IconFileImport size={18} />}
            disabled={!!busyReason}
            onClick={() => setOpen('import')}
          >
            {t('mods.actions.import')}
          </Button>
        </Why>
        <Why reason={busyReason}>
          <Button
            variant="default"
            leftSection={<IconPlus size={18} />}
            disabled={!!busyReason}
            onClick={() => setOpen('add')}
          >
            {t('mods.actions.add')}
          </Button>
        </Why>
        <Why reason={noActive}>
          <Button
            variant="default"
            leftSection={<IconRefresh size={18} />}
            loading={check.isPending}
            disabled={!!noActive}
            onClick={() => check.mutate()}
          >
            {t('mods.actions.check')}
          </Button>
        </Why>
        {updates.length > 0 && (
          <Why reason={busyReason}>
            <Button
              variant="light"
              leftSection={<IconUpload size={18} />}
              loading={sync.isPending}
              disabled={!!busyReason}
              onClick={() =>
                sync.mutate({ entries: withUpdates(chosenEntries(mods.active), updates) })
              }
            >
              {t('mods.actions.updateAll', { count: updates.length })}
            </Button>
          </Why>
        )}
        <Why reason={hasMods ? null : t('mods.noActive')}>
          <Button
            variant="default"
            leftSection={<IconShare size={18} />}
            disabled={!hasMods}
            onClick={() => setOpen('share')}
          >
            {t('mods.actions.share')}
          </Button>
        </Why>
        <Why reason={busyReason ?? (mods.previous ? null : t('mods.noPrevious'))}>
          <Button
            variant="subtle"
            leftSection={<IconArrowBackUp size={18} />}
            loading={rollback.isPending}
            disabled={!!busyReason || !mods.previous}
            onClick={confirmRollback}
          >
            {t('mods.actions.rollback')}
          </Button>
        </Why>
      </Group>
      {mods.newerVersions && (
        <Text size="xs" c="dimmed">
          {t('mods.checked', { time: dayjs(mods.newerVersions.checkedAt).fromNow() })}
        </Text>
      )}
      <ImportCodeModal id={id} server={server} opened={open === 'import'} onClose={close} />
      <AddModModal id={id} server={server} mods={mods} opened={open === 'add'} onClose={close} />
      <ShareCodeModal id={id} mods={mods} opened={open === 'share'} onClose={close} />
    </>
  );
}
