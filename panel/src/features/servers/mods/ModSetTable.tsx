import { useEffect, useState } from 'react';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { Stack } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import type { ModEntryView, ModsView } from '@/api/generated';
import { chosenEntries } from './modEntries';
import { filterEntries, type ModFilter } from './modFilter';
import { ModSelectionBar } from './ModSelectionBar';
import { ModsMobileList } from './ModsMobileList';
import { useModColumns } from './useModColumns';
import { useModsSync } from './useModsSync';

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
  const isPhone = useMediaQuery('(max-width: 48em)');

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

  const noRecords = query.trim()
    ? t('mods.noMatches', { query })
    : filter === 'all'
      ? t('mods.empty')
      : t('mods.filterEmpty');
  const columns = useModColumns({
    community: mods.community,
    newer,
    states,
    busy,
    busyReason,
    chosen,
    onChange: change,
  });

  return (
    <Stack gap={0}>
      {selected.length > 0 && (
        <ModSelectionBar
          count={selected.length}
          busy={busy}
          hasUpdates={selectedUpdates.length > 0}
          onEnable={bulk.enable}
          onDisable={bulk.disable}
          onUpdate={bulk.update}
          onRemove={bulk.remove}
          onClear={() => setSelected([])}
        />
      )}
      {isPhone ? (
        <ModsMobileList
          entries={visible}
          newer={newer}
          states={states}
          busy={busy}
          busyReason={busyReason}
          community={mods.community}
          checked={selected}
          onCheckedChange={setSelected}
          chosen={chosen}
          onChange={change}
        />
      ) : (
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
          columns={columns}
        />
      )}
    </Stack>
  );
}
