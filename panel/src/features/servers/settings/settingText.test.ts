import i18n from 'i18next';
import '@/i18n';
import { valheimSettings } from '@/mocks/valheimSettings';
import { localized, settingsSummary } from './settingText';

const t = i18n.t;
const modifiers = valheimSettings.filter((setting) => setting.group === 'modifiers');

describe('setting text', () => {
  beforeAll(() => i18n.changeLanguage('en'));

  it('uses the language asked, else English', () => {
    expect(localized({ en: 'World', vi: 'Thế giới' }, 'vi')).toBe('Thế giới');
    expect(localized({ en: 'World' }, 'vi')).toBe('World');
    expect(localized(null, 'en')).toBeUndefined();
  });

  it('sums up only what differs from the defaults', () => {
    const settings = Object.fromEntries(
      modifiers.map((setting) => [setting.key, setting.default ?? ''])
    );
    expect(settingsSummary(modifiers, { settings, secrets: {} }, 'en', t)).toBe(
      'All at their defaults'
    );

    const changed = { ...settings, raids: 'none', noMap: 'true' };
    expect(settingsSummary(modifiers, { settings: changed, secrets: {} }, 'en', t)).toBe(
      'Raids: None · No map'
    );
    expect(settingsSummary(modifiers, { settings: changed, secrets: {} }, 'vi', t)).toBe(
      'Đột kích: Không có · Không có bản đồ'
    );
  });

  it('never shows a secret (docs/11 §10 rule 7)', () => {
    const server = valheimSettings.filter((setting) => setting.group === 'server');
    const summary = settingsSummary(
      server,
      {
        settings: { serverName: 'Vikings', crossplay: 'false' },
        secrets: { password: 'hunter22' },
      },
      'en',
      t
    );
    expect(summary).toBe('Name in Valheim: Vikings · Password: ••••');
    expect(summary).not.toContain('hunter22');
  });
});
