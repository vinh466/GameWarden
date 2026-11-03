import type { ModEntryView } from '@/api/generated';
import { displayName } from './modEntries';

/** How the Mods table is narrowed (docs/13 §4.10): everything, by the switch, or the mods a check found newer. */
export type ModFilter = 'all' | 'enabled' | 'disabled' | 'updates';

/**
 * The rows the table shows. A query matches the package (`RandyKnapp-EpicLoot`) or the name Thunderstore shows
 * (`EpicLoot`), case-insensitively; the filter is applied first. Dependencies stay visible because they explain what a
 * set contains.
 */
export function filterEntries(
  entries: ModEntryView[],
  filter: ModFilter,
  query: string,
  newer: ReadonlyMap<string, string>
): ModEntryView[] {
  const text = query.trim().toLowerCase();
  return entries.filter((entry) => {
    if (filter === 'enabled' && !entry.enabled) {
      return false;
    }

    if (filter === 'disabled' && entry.enabled) {
      return false;
    }

    if (filter === 'updates' && !newer.has(entry.package)) {
      return false;
    }

    return (
      text.length === 0 ||
      entry.package.toLowerCase().includes(text) ||
      displayName(entry.package).toLowerCase().includes(text)
    );
  });
}
