import dayjs from 'dayjs';
import { useState } from 'react';
import {
  IconArrowBackUp,
  IconDotsVertical,
  IconFileImport,
  IconPlus,
  IconRefresh,
  IconSearch,
  IconShare,
  IconUpload,
} from '@tabler/icons-react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Button,
  Group,
  Menu,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
} from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { checkInstanceModsUpdates, rollbackInstanceMods, type ModsView } from '@/api/generated';
import { queries } from '@/api/queries';
import { AddModModal } from './AddModModal';
import { ImportCodeModal } from './ImportCodeModal';
import { chosenEntries, chosenUpdates, withUpdates } from './modEntries';
import type { ModFilter } from './modFilter';
import { ShareCodeModal } from './ShareCodeModal';
import { refreshMods, useModsFailure, useModsSync } from './useModsSync';
import { Why } from './Why';

/**
 * The Mods screen's toolbar (docs/13 §4.10): search and filter over the set, then the primary actions. Check, share
 * and rollback sit behind the overflow menu so the row stays short; *Update all* appears only when it would do
 * something.
 */
export function ModActions({
  id,
  server,
  mods,
  busyReason,
  query,
  onQueryChange,
  filter,
  onFilterChange,
}: {
  id: string;
  server: string;
  mods: ModsView;
  busyReason: string | null;
  query: string;
  onQueryChange: (value: string) => void;
  filter: ModFilter;
  onFilterChange: (value: ModFilter) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const failed = useModsFailure();
  const sync = useModsSync(id, server);
  const [open, setOpen] = useState<'import' | 'add' | 'share' | null>(null);
  const close = () => setOpen(null);
  const updates = chosenUpdates(mods);
  const hasMods = (mods.active?.entries ?? []).some((entry) => entry.enabled);
  const noActive = mods.active ? null : t('mods.noActive');

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

  return (
    <Stack gap="sm" p="md" pb="sm">
      <Group justify="space-between" align="center" gap="sm" wrap="wrap">
        <Group gap="xs" wrap="wrap">
          <TextInput
            value={query}
            onChange={(event) => onQueryChange(event.currentTarget.value)}
            placeholder={t('mods.searchPlaceholder')}
            aria-label={t('mods.search')}
            leftSection={<IconSearch size={16} aria-hidden />}
            w={{ base: '100%', sm: 260 }}
          />
          <SegmentedControl
            size="xs"
            value={filter}
            onChange={(value) => onFilterChange(value as ModFilter)}
            data={[
              { value: 'all', label: t('mods.filter.all') },
              { value: 'enabled', label: t('mods.filter.enabled') },
              { value: 'disabled', label: t('mods.filter.disabled') },
              { value: 'updates', label: t('mods.filter.updates') },
            ]}
          />
        </Group>

        <Group gap="xs" wrap="wrap">
          <Why reason={busyReason}>
            <Button
              leftSection={<IconPlus size={18} />}
              disabled={!!busyReason}
              onClick={() => setOpen('add')}
            >
              {t('mods.actions.add')}
            </Button>
          </Why>
          <Why reason={busyReason}>
            <Button
              variant="default"
              leftSection={<IconFileImport size={18} />}
              disabled={!!busyReason}
              onClick={() => setOpen('import')}
            >
              {t('mods.actions.import')}
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
          <Menu position="bottom-end" withinPortal>
            <Menu.Target>
              <ActionIcon variant="default" size="lg" aria-label={t('mods.actions.more')}>
                <IconDotsVertical size={18} aria-hidden />
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown>
              {busyReason && <Menu.Label maw={280}>{busyReason}</Menu.Label>}
              {!noActive && <Menu.Label maw={280}>{t('mods.noActive')}</Menu.Label>}
              <Menu.Item
                leftSection={<IconRefresh size={16} />}
                disabled={!!noActive || check.isPending}
                onClick={() => check.mutate()}
              >
                {t('mods.actions.check')}
              </Menu.Item>
              <Menu.Item
                leftSection={<IconShare size={16} />}
                disabled={!hasMods}
                onClick={() => setOpen('share')}
              >
                {t('mods.actions.share')}
              </Menu.Item>
              <Menu.Divider />
              <Menu.Item
                leftSection={<IconArrowBackUp size={16} />}
                disabled={!!busyReason || !mods.previous || rollback.isPending}
                onClick={confirmRollback}
              >
                {t('mods.actions.rollback')}
              </Menu.Item>
              {!mods.previous && !busyReason && (
                <Menu.Label maw={280}>{t('mods.noPrevious')}</Menu.Label>
              )}
            </Menu.Dropdown>
          </Menu>
        </Group>
      </Group>
      {mods.newerVersions && (
        <Text size="xs" c="dimmed">
          {t('mods.checked', { time: dayjs(mods.newerVersions.checkedAt).fromNow() })}
        </Text>
      )}
      <ImportCodeModal id={id} server={server} opened={open === 'import'} onClose={close} />
      <AddModModal id={id} server={server} mods={mods} opened={open === 'add'} onClose={close} />
      <ShareCodeModal id={id} mods={mods} opened={open === 'share'} onClose={close} />
    </Stack>
  );
}
