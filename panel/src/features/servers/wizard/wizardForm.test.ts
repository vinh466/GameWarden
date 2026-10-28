import { sectionFields, settingsSummary, type WizardValues } from './wizardForm';

const values: WizardValues = {
  displayName: 'My server',
  id: 'my-server',
  settings: { serverName: 'MyServer', world: 'Midgard' },
  secrets: { password: 'hunter22', adminToken: '' },
};

describe('wizard form', () => {
  it('gives each section its own fields', () => {
    expect(sectionFields('game', values)).toEqual([]);
    expect(sectionFields('name', values)).toEqual(['displayName', 'id']);
    expect(sectionFields('settings', values)).toEqual([
      'settings.serverName',
      'settings.world',
      'secrets.password',
      'secrets.adminToken',
    ]);
  });

  it('sums up settings without showing a secret (docs/11 §10 rule 7)', () => {
    const summary = settingsSummary(values);
    expect(summary).toBe('serverName: MyServer · world: Midgard · password: ••••');
    expect(summary).not.toContain('hunter22');
  });
});
