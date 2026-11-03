import { useState } from 'react';
import { IconPuzzle } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Card } from '@mantine/core';
import type { ModsView } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';
import { ModActions } from './ModActions';
import type { ModFilter } from './modFilter';
import { ModSetTable } from './ModSetTable';

/**
 * The set's list and its toolbar (docs/13 §4.10). The status strip above says what the set is; this card is where it
 * is searched, filtered and changed. An empty set keeps the two ways to start: a code or one mod at a time.
 */
export function ActiveSetCard({
  id,
  server,
  mods,
}: {
  id: string;
  server: string;
  mods: ModsView;
}) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<ModFilter>('all');
  const busyReason = mods.pending?.state === 'building' ? t('mods.busyReason') : null;

  return (
    <Card p={0}>
      <ModActions
        id={id}
        server={server}
        mods={mods}
        busyReason={busyReason}
        query={query}
        onQueryChange={setQuery}
        filter={filter}
        onFilterChange={setFilter}
      />
      {mods.active ? (
        <ModSetTable
          id={id}
          server={server}
          mods={mods}
          busyReason={busyReason}
          query={query}
          filter={filter}
        />
      ) : (
        <EmptyState icon={IconPuzzle} title={t('mods.empty')} text={t('mods.emptyText')} />
      )}
    </Card>
  );
}
