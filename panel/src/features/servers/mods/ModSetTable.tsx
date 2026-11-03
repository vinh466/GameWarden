import { useEffect, useState } from 'react';
import {
  IconDots,
  IconPlayerPause,
  IconPlayerPlay,
  IconTrash,
  IconUpload,
  IconX,
} from '@tabler/icons-react';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Anchor,
  Badge,
  Button,
  Group,
  Menu,
  Paper,
  Stack,
  Text,
  Tooltip,
} from '@mantine/core';
import type { ModEntryView, ModPackageState, ModsView } from '@/api/generated';
import {
  chosenEntries,
  displayName,
  packageUrl,
  withEnabled,
  withMod,
  withoutMod,
} from './modEntries';
import { filterEntries, type ModFilter } from './modFilter';
import { useModsSync } from './useModsSync';

const stateColors: Record<ModPackageState, string> = {
  ok: 'gray',
  deprecated: 'yellow',
  notFound: 'red',
};

const PAGE_SIZE = 25;

/**
 * The active set (docs/13 §4.10): each mod with its version, a newer one after a check, *enabled*, the dependency badge
 * and Thunderstore's state. Search and the filter narrow the list; row and bulk actions make one new set from the
 * chosen mods, as the CLI does. A dependency follows the mods that need it, so it is not selectable and its actions
 * are off with the reason.
 */
export function ModSetTable({
  id,
  server,
  mods,
  busyReason,
  query,
  filter,
}: {
  id: string;
  server: string;
  mods: ModsView;
  busyReason: string | null;
  query: string;
  filter: ModFilter;
}) {
  const { t } = useTranslation();
  const sync = useModsSync(id, server);
  const entries = mods.active?.entries ?? [];
  const newer = new Map((mods.newerVersions?.updates ?? []).map((u) => [u.package, u.latest]));
  const states = new Map((mods.newerVersions?.states ?? []).map((s) => [s.package, s.state]));
  const chosen = chosenEntries(mods.active);
  const visible = filterEntries(entries, filter, query, newer);
  const [selected, setSelected] = useState<ModEntryView[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZE);
  const busy = !!busyReason || sync.isPending;

  useEffect(() => {
    setSelected([]);
    setPage(1);
  }, [mods.active?.id, query, filter]);

  useEffect(() => {
    const pages = Math.max(1, Math.ceil(visible.length / pageSize));
    setPage((current) => Math.min(current, pages));
  }, [visible.length, pageSize]);

  const change = (next: typeof chosen) => {
    sync.mutate({ entries: next });
    setSelected([]);
  };

  const selectedPackages = new Set(selected.map((entry) => entry.package));
  const selectedUpdates = selected.filter((entry) => newer.has(entry.package));
  const bulk = {
    enable: () =>
      change(
        chosen.map((entry) =>
          selectedPackages.has(entry.package) ? { ...entry, enabled: true } : entry
        )
      ),
    disable: () =>
      change(
        chosen.map((entry) =>
          selectedPackages.has(entry.package) ? { ...entry, enabled: false } : entry
        )
      ),
    update: () =>
      change(
        chosen.map((entry) => {
          const latest = newer.get(entry.package);
          return selectedPackages.has(entry.package) && latest
            ? { ...entry, version: latest }
            : entry;
        })
      ),
    remove: () => change(chosen.filter((entry) => !selectedPackages.has(entry.package))),
  };

  const stateBadge = (entry: ModEntryView) => {
    const state = states.get(entry.package);
    return state ? (
      <Badge variant="light" color={stateColors[state]}>
        {t(`mods.state.${state}`)}
      </Badge>
    ) : (
      <Tooltip label={t('mods.stateUnknownHint')}>
        <Text size="xs" c="dimmed" span>
          {t('mods.state.unknown')}
        </Text>
      </Tooltip>
    );
  };

  const noRecords = query.trim()
    ? t('mods.noMatches', { query })
    : filter === 'all'
      ? t('mods.empty')
      : t('mods.filterEmpty');

  return (
    <Stack gap={0}>
      {selected.length > 0 && (
        <Paper p="xs" bg="var(--mantine-color-blue-light)" radius={0}>
          <Group justify="space-between" gap="xs" wrap="wrap">
            <Text size="sm" fw={500}>
              {t('mods.selection.count', { count: selected.length })}
            </Text>
            <Group gap="xs" wrap="wrap">
              <Button size="xs" variant="light" disabled={busy} onClick={bulk.enable}>
                {t('mods.selection.enable')}
              </Button>
              <Button size="xs" variant="light" disabled={busy} onClick={bulk.disable}>
                {t('mods.selection.disable')}
              </Button>
              {selectedUpdates.length > 0 && (
                <Button
                  size="xs"
                  variant="light"
                  color="violet"
                  disabled={busy}
                  onClick={bulk.update}
                >
                  {t('mods.selection.update')}
                </Button>
              )}
              <Button size="xs" variant="light" color="red" disabled={busy} onClick={bulk.remove}>
                {t('mods.selection.remove')}
              </Button>
              <Button
                size="xs"
                variant="subtle"
                color="gray"
                leftSection={<IconX size={14} />}
                onClick={() => setSelected([])}
              >
                {t('mods.selection.clear')}
              </Button>
            </Group>
          </Group>
        </Paper>
      )}
      <DataTable
        records={visible}
        idAccessor="package"
        fetching={sync.isPending}
        minHeight={visible.length === 0 ? 120 : undefined}
        noRecordsText={noRecords}
        page={page}
        onPageChange={setPage}
        totalRecords={visible.length}
        recordsPerPage={pageSize}
        onRecordsPerPageChange={(size) => {
          setPageSize(size);
          setPage(1);
        }}
        recordsPerPageOptions={[10, 25, 50]}
        selectedRecords={selected}
        onSelectedRecordsChange={setSelected}
        isRecordSelectable={(entry) => entry.origin === 'chosen' && !busy}
        highlightOnHover
        columns={[
          {
            accessor: 'package',
            title: t('mods.columns.mod'),
            render: (entry) => (
              <Stack gap={2}>
                <Group gap={6} wrap="wrap">
                  <Anchor
                    href={packageUrl(mods.community, entry.package)}
                    target="_blank"
                    size="sm"
                    fw={500}
                  >
                    {displayName(entry.package)}
                  </Anchor>
                  {entry.origin === 'dependency' && (
                    <Tooltip label={t('mods.dependencyHint')}>
                      <Badge size="xs" variant="outline" color="gray">
                        {t('mods.dependency')}
                      </Badge>
                    </Tooltip>
                  )}
                </Group>
                <Text size="xs" c="dimmed" ff="monospace">
                  {entry.package}
                  <Text span hiddenFrom="sm" inherit>
                    {` · ${entry.version}`}
                    {newer.has(entry.package) ? ` → ${newer.get(entry.package)}` : ''}
                  </Text>
                </Text>
                <Text size="xs" hiddenFrom="sm" c={entry.enabled ? 'green' : 'dimmed'}>
                  {entry.enabled ? t('mods.enabledYes') : t('mods.enabledNo')}
                </Text>
              </Stack>
            ),
          },
          {
            accessor: 'version',
            title: t('mods.columns.version'),
            visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
            render: (entry) => (
              <Text size="sm" ff="monospace">
                {entry.version}
              </Text>
            ),
          },
          {
            accessor: 'newer',
            title: t('mods.columns.newer'),
            visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
            render: (entry) =>
              newer.has(entry.package) ? (
                <Badge variant="light" color="violet" ff="monospace">
                  {newer.get(entry.package)}
                </Badge>
              ) : (
                <Text size="sm" c="dimmed">
                  —
                </Text>
              ),
          },
          {
            accessor: 'enabled',
            title: t('mods.columns.enabled'),
            visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
            render: (entry) => (
              <Badge variant="light" color={entry.enabled ? 'green' : 'gray'}>
                {entry.enabled ? t('mods.enabledYes') : t('mods.enabledNo')}
              </Badge>
            ),
          },
          {
            accessor: 'state',
            title: t('mods.columns.state'),
            visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
            render: stateBadge,
          },
          {
            accessor: 'actions',
            title: '',
            textAlign: 'right',
            render: (entry) => {
              const dependency = entry.origin === 'dependency';
              const off = dependency || busy;
              const latest = newer.get(entry.package);
              return (
                <Menu position="bottom-end" withinPortal>
                  <Menu.Target>
                    <ActionIcon
                      variant="subtle"
                      color="gray"
                      aria-label={t('mods.rowActions', { mod: entry.package })}
                    >
                      <IconDots size={18} aria-hidden />
                    </ActionIcon>
                  </Menu.Target>
                  <Menu.Dropdown>
                    {(dependency || busyReason) && (
                      <Menu.Label maw={260}>{busyReason ?? t('mods.dependencyActions')}</Menu.Label>
                    )}
                    <Menu.Item
                      disabled={off}
                      leftSection={
                        entry.enabled ? <IconPlayerPause size={16} /> : <IconPlayerPlay size={16} />
                      }
                      onClick={() => change(withEnabled(chosen, entry.package, !entry.enabled))}
                    >
                      {entry.enabled ? t('mods.disable') : t('mods.enable')}
                    </Menu.Item>
                    {latest && (
                      <Menu.Item
                        disabled={off}
                        leftSection={<IconUpload size={16} />}
                        onClick={() =>
                          change(withMod(chosen, { package: entry.package, version: latest }))
                        }
                      >
                        {t('mods.updateTo', { version: latest })}
                      </Menu.Item>
                    )}
                    <Menu.Item
                      disabled={off}
                      color="red"
                      leftSection={<IconTrash size={16} />}
                      onClick={() => change(withoutMod(chosen, entry.package))}
                    >
                      {t('mods.remove')}
                    </Menu.Item>
                  </Menu.Dropdown>
                </Menu>
              );
            },
          },
        ]}
      />
    </Stack>
  );
}
