import type { AdapterSummary } from '@/api/generated';
import { system } from '@/mocks/data';
import { valheimSettings } from '@/mocks/valheimSettings';
import { blockingChecks } from './ReviewChecks';

const adapter: AdapterSummary = {
  id: 'valheim',
  name: 'Valheim',
  version: '0.5.1',
  level: 'L3',
  isBuiltIn: true,
  sourceType: 'steamcmd',
  ports: [
    { name: 'game', protocol: 'udp', defaultPort: 2456 },
    { name: 'query', protocol: 'udp', defaultPort: 2457 },
  ],
  requiredSettings: ['serverName', 'world'],
  requiredSecrets: ['password'],
  prerequisites: ['vcredist-x64'],
  minResources: { cpuCores: 2, ramMb: 2048, diskMb: 2000 },
  recommendedResources: { cpuCores: 4, ramMb: 4096, diskMb: 4000 },
  settingGroups: [],
  settings: valheimSettings,
};

describe('blockingChecks', () => {
  it('passes when the machine meets the minimum and the prerequisite is installed', () => {
    expect(blockingChecks(adapter, system)).toEqual([]);
  });

  it('names each red check so the wizard can label Install anyway', () => {
    expect(
      blockingChecks(adapter, {
        ...system,
        machine: { ...system.machine, ramFreeMb: 1024 },
        prerequisites: system.prerequisites.map((item) => ({ ...item, installed: false })),
      })
    ).toEqual(['ram', 'vcredist-x64']);
  });
});
