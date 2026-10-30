import {
  IconDots,
  IconPlayerPause,
  IconPlayerPlay,
  IconTrash,
  IconUpload,
} from '@tabler/icons-react';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Anchor, Badge, Group, Menu, Stack, Text, Tooltip } from '@mantine/core';
import type { ModEntryView, ModPackageState, ModsView } from '@/api/generated';
import {
  chosenEntries,
  displayName,
  packageUrl,
  withEnabled,
  withMod,
  withoutMod,
} from './modEntries';
import { useModsSync } from './useModsSync';

const stateColors: Record<ModPackageState, string> = {
  ok: 'gray',
  deprecated: 'yellow',
  notFound: 'red',
};

/**
 * The active set (docs/13 §4.10): each mod with its version, a newer one after a check, *enabled*, the dependency badge
 * and Thunderstore's state. A row's actions make a new set from the chosen mods, as the CLI does; a dependency follows
 * the mods that need it, so its actions are off with the reason.
 */
export function ModSetTable({
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
  const sync = useModsSync(id, server);
  const entries = mods.active?.entries ?? [];
  const newer = new Map((mods.newerVersions?.updates ?? []).map((u) => [u.package, u.latest]));
  const states = new Map((mods.newerVersions?.states ?? []).map((s) => [s.package, s.state]));
  const chosen = chosenEntries(mods.active);
  const change = (next: typeof chosen) => sync.mutate({ entries: next });

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

  return (
    <DataTable
      records={entries}
      idAccessor="package"
      fetching={sync.isPending}
      minHeight={entries.length === 0 ? 120 : undefined}
      noRecordsText={t('mods.empty')}
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
          render: (entry) => (
            <Text size="sm" c={entry.enabled ? undefined : 'dimmed'}>
              {entry.enabled ? t('mods.enabledYes') : t('mods.enabledNo')}
            </Text>
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
            const off = dependency || !!busyReason || sync.isPending;
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
  );
}
