import type { ModsView } from '@/api/generated';
import { initialMods } from '@/mocks/mods';
import {
  chosenEntries,
  chosenUpdates,
  displayName,
  packageUrl,
  parsePackage,
  withEnabled,
  withMod,
  withoutMod,
  withUpdates,
} from './modEntries';

const mods = initialMods()['valheim-main'] as ModsView;

describe('mod set changes', () => {
  it('send the chosen mods only, with their versions and enabled', () => {
    expect(chosenEntries(mods.active)).toEqual([
      { package: 'RandyKnapp-EpicLoot', version: '0.15.3', enabled: true },
      { package: 'SephrinMods-VikingNPC_Continued', version: '0.4.2', enabled: true },
      { package: 'Advize-PlantEverything', version: '1.21.4', enabled: false },
    ]);
    expect(chosenEntries(null)).toEqual([]);
  });

  it('add, pin, disable and remove a mod', () => {
    const chosen = chosenEntries(mods.active);
    expect(withMod(chosen, { package: 'shudnal-Seasons', version: '1.10.5' }).at(-1)).toEqual({
      package: 'shudnal-Seasons',
      version: '1.10.5',
    });
    expect(withMod(chosen, { package: 'RandyKnapp-EpicLoot', version: '0.15.4' })[0]).toEqual({
      package: 'RandyKnapp-EpicLoot',
      version: '0.15.4',
      enabled: true,
    });
    expect(withEnabled(chosen, 'Advize-PlantEverything', true)[2]?.enabled).toBe(true);
    expect(withoutMod(chosen, 'RandyKnapp-EpicLoot').map((entry) => entry.package)).toEqual([
      'SephrinMods-VikingNPC_Continued',
      'Advize-PlantEverything',
    ]);
  });

  it('update all changes the chosen mods; dependencies follow them', () => {
    const updates = chosenUpdates(mods);
    expect(updates.map((update) => update.package)).toEqual(['RandyKnapp-EpicLoot']);
    expect(withUpdates(chosenEntries(mods.active), updates)[0]?.version).toBe('0.15.4');
  });

  it('read Namespace-Name with an optional exact version', () => {
    expect(parsePackage(' RandyKnapp-EpicLoot ')).toEqual({
      package: 'RandyKnapp-EpicLoot',
      version: null,
    });
    expect(parsePackage('RandyKnapp-EpicLoot@0.15.4')).toEqual({
      package: 'RandyKnapp-EpicLoot',
      version: '0.15.4',
    });
    for (const bad of [
      'EpicLoot',
      'RandyKnapp-EpicLoot@latest',
      'a-b@1.2',
      '../x-y',
      'a-b@1.0.0@2',
    ]) {
      expect(parsePackage(bad)).toBeNull();
    }
  });

  it('link to Thunderstore and name a package as it shows it', () => {
    expect(packageUrl('valheim', 'RandyKnapp-EpicLoot')).toBe(
      'https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/'
    );
    expect(packageUrl(null, 'RandyKnapp-EpicLoot')).toBe(
      'https://thunderstore.io/package/RandyKnapp/EpicLoot/'
    );
    expect(displayName('SephrinMods-VikingNPC_Continued')).toBe('VikingNPC Continued');
  });
});
