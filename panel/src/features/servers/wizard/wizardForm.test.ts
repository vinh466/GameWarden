import { adapters } from '@/mocks/data';
import { sectionFields, settingsPlaceholder, wizardFields, wizardSectionIds } from './wizardForm';

const valheim = adapters[0]!;

describe('wizard form', () => {
  it('asks only what the game needs to start, plus what the adapter marks for the wizard', () => {
    expect(wizardFields(valheim).map((setting) => setting.key)).toEqual([
      'serverName',
      'password',
      'world',
      'preset',
    ]);
  });

  it('has one section per settings group with something to ask', () => {
    expect(wizardSectionIds(null)).toEqual(['game', 'name', settingsPlaceholder, 'review']);
    expect(wizardSectionIds(valheim)).toEqual([
      'game',
      'name',
      'group:server',
      'group:world',
      'review',
    ]);
  });

  it('gives each section its own fields, secrets apart', () => {
    expect(sectionFields('game', valheim)).toEqual([]);
    expect(sectionFields('name', valheim)).toEqual(['displayName', 'id']);
    expect(sectionFields('group:server', valheim)).toEqual([
      'settings.serverName',
      'secrets.password',
    ]);
  });
});
