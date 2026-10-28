import i18n from 'i18next';
import '@/i18n';
import { valheimSettings } from '@/mocks/valheimSettings';
import { initialSettingValues, settingProblem } from './settingSchema';

const t = i18n.t;
const field = (key: string) => valheimSettings.find((setting) => setting.key === key)!;

describe('setting rules (as Core checks them)', () => {
  beforeAll(() => i18n.changeLanguage('en'));

  it.each([
    ['preset', 'brutal', 'Pick one of the choices'],
    ['crossplay', 'yes', 'Pick one of the choices'],
    ['saveInterval', '10', 'From 60 to 7200'],
    ['saveInterval', 'ten', 'A whole number'],
    ['password', 'abc', 'At least 5 characters'],
    ['world', 'Mid/gard', 'Contains characters that are not allowed'],
    ['serverName', '', 'Required by the game'],
  ])('refuses %s = "%s"', (key, value, message) => {
    expect(settingProblem(field(key), value, t)).toBe(message);
  });

  it.each([
    ['preset', 'hard'],
    ['crossplay', 'true'],
    ['saveInterval', '600'],
    ['world', 'Midgard'],
    ['raids', ''],
  ])('accepts %s = "%s"', (key, value) => {
    expect(settingProblem(field(key), value, t)).toBeNull();
  });

  it('lets an empty secret mean "unchanged" while editing', () => {
    expect(settingProblem(field('password'), '', t)).toBe('Required by the game');
    expect(settingProblem(field('password'), '', t, true)).toBeNull();
  });

  it('starts from the stored value, else the default; secrets empty', () => {
    const values = initialSettingValues(valheimSettings, { world: 'Midgard', password: 'leak' });
    expect(values.settings.world).toBe('Midgard');
    expect(values.settings.preset).toBe('normal');
    expect(values.settings.saveInterval).toBe('600');
    expect(values.settings.serverName).toBe('');
    expect(values.secrets).toEqual({ password: '' });
  });
});
