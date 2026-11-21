import type { DataTableColumn } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { Anchor, Badge, Group, Stack, Text, Tooltip } from '@mantine/core';
import type { ModEntryRequest, ModEntryView, ModPackageState } from '@/api/generated';
import { displayName, packageUrl, withEnabled, withMod, withoutMod } from './modEntries';
import { ModRowMenu } from './ModRowMenu';

const stateColors: Record<ModPackageState, string> = {
  ok: 'gray',
  deprecated: 'yellow',
  notFound: 'red',
};

/** The active set's columns: mod, version, newer version, enabled, Thunderstore state and the row actions. */
export function useModColumns({
  community,
  newer,
  states,
  busy,
  busyReason,
  chosen,
  onChange,
}: {
  community: string | null;
  newer: Map<string, string>;
  states: Map<string, ModPackageState>;
  busy: boolean;
  busyReason: string | null;
  chosen: ModEntryRequest[];
  onChange: (next: ModEntryRequest[]) => void;
}): DataTableColumn<ModEntryView>[] {
  const { t } = useTranslation();

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

  return [
    {
      accessor: 'package',
      title: t('mods.columns.mod'),
      render: (entry) => (
        <Stack gap={2}>
          <Group gap={6} wrap="wrap">
            <Anchor href={packageUrl(community, entry.package)} target="_blank" size="sm" fw={500}>
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
      render: (entry) => (
        <ModRowMenu
          entry={entry}
          dependency={entry.origin === 'dependency'}
          busy={busy}
          busyReason={busyReason}
          latest={newer.get(entry.package)}
          onToggleEnabled={() => onChange(withEnabled(chosen, entry.package, !entry.enabled))}
          onUpdate={(version) => onChange(withMod(chosen, { package: entry.package, version }))}
          onRemove={() => onChange(withoutMod(chosen, entry.package))}
        />
      ),
    },
  ];
}
