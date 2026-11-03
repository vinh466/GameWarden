import type { ModEntryView } from '@/api/generated';
import { filterEntries } from './modFilter';

const entries: ModEntryView[] = [
  {
    package: 'RandyKnapp-EpicLoot',
    version: '0.15.4',
    enabled: true,
    origin: 'chosen',
  },
  {
    package: 'Advize-PlantEverything',
    version: '1.21.4',
    enabled: false,
    origin: 'chosen',
  },
  {
    package: 'ValheimModding-Jotunn',
    version: '2.30.2',
    enabled: true,
    origin: 'dependency',
  },
];

const newer = new Map([['RandyKnapp-EpicLoot', '0.15.5']]);

describe('the Mods table filter', () => {
  it('keeps everything with no query and the all filter', () => {
    expect(filterEntries(entries, 'all', '', newer)).toEqual(entries);
  });

  it('matches the package or the name Thunderstore shows, case-insensitively', () => {
    expect(filterEntries(entries, 'all', 'epicloot', newer).map((entry) => entry.package)).toEqual([
      'RandyKnapp-EpicLoot',
    ]);
    expect(filterEntries(entries, 'all', 'RandyKnapp', newer)).toHaveLength(1);
    expect(filterEntries(entries, 'all', 'nope', newer)).toEqual([]);
  });

  it('filters by the switch and by a newer version', () => {
    expect(filterEntries(entries, 'enabled', '', newer).map((entry) => entry.package)).toEqual([
      'RandyKnapp-EpicLoot',
      'ValheimModding-Jotunn',
    ]);
    expect(filterEntries(entries, 'disabled', '', newer).map((entry) => entry.package)).toEqual([
      'Advize-PlantEverything',
    ]);
    expect(filterEntries(entries, 'updates', '', newer).map((entry) => entry.package)).toEqual([
      'RandyKnapp-EpicLoot',
    ]);
  });
});
