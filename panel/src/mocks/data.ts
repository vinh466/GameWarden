import type {
  AdapterSummary,
  InstanceSnapshot,
  JobInfo,
  OutputLine,
  SettingsView,
  SystemInfo,
} from '@/api/generated';

// Mock state for `npm run dev:mock` and component tests: shaped like Core's answers (generated types), invented values.
const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

export const adapters: AdapterSummary[] = [
  {
    id: 'valheim',
    name: 'Valheim',
    version: '0.2.0',
    level: 'L1',
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
  },
];

export function initialInstances(): InstanceSnapshot[] {
  return [
    {
      id: 'valheim-main',
      displayName: 'Vikings of the North',
      adapterId: 'valheim',
      desiredState: 'running',
      state: 'running',
      activeJobId: null,
      runnerPid: 7412,
      gamePid: 7436,
      recentRestarts: 0,
      lastProblem: null,
      ports: { game: 2456, query: 2457 },
    },
    {
      id: 'valheim-hardcore',
      displayName: 'Hardcore weekend',
      adapterId: 'valheim',
      desiredState: 'stopped',
      state: 'stopped',
      activeJobId: null,
      runnerPid: null,
      gamePid: null,
      recentRestarts: 0,
      lastProblem: 'exited with code 3',
      ports: { game: 2466, query: 2467 },
    },
    {
      id: 'valheim-test',
      displayName: 'Test world',
      adapterId: 'valheim',
      desiredState: 'stopped',
      state: 'notInstalled',
      activeJobId: '20261028-101500-a1b2c3',
      runnerPid: null,
      gamePid: null,
      recentRestarts: 0,
      lastProblem: null,
      ports: { game: 2476, query: 2477 },
    },
  ];
}

export function initialJobs(): JobInfo[] {
  return [
    {
      id: '20261028-101500-a1b2c3',
      type: 'install',
      instanceId: 'valheim-test',
      state: 'running',
      stage: 'steamcmd.install',
      progress: 37.5,
      message: null,
      createdAt: minutesAgo(2),
      finishedAt: null,
    },
    {
      id: '20261028-040000-9f8e7d',
      type: 'update',
      instanceId: 'valheim-main',
      state: 'succeeded',
      stage: 'start',
      progress: 100,
      message: 'Installed; the server is starting again.',
      createdAt: minutesAgo(380),
      finishedAt: minutesAgo(376),
    },
    {
      id: '20261027-213000-4c5d6e',
      type: 'install',
      instanceId: 'valheim-hardcore',
      state: 'failed',
      stage: 'steamcmd.install',
      progress: 12,
      message: "install failed: ERROR! Failed to install app '896660' (No connection)",
      createdAt: minutesAgo(800),
      finishedAt: minutesAgo(797),
    },
    {
      id: '20261027-200000-1a2b3c',
      type: 'install',
      instanceId: 'valheim-main',
      state: 'succeeded',
      stage: 'start',
      progress: 100,
      message: 'Installed.',
      createdAt: minutesAgo(900),
      finishedAt: minutesAgo(893),
    },
  ];
}

export function initialSettings(): Record<string, SettingsView> {
  return {
    'valheim-main': {
      settings: { serverName: 'Vikings of the North', world: 'Midgard' },
      requiredSettings: ['serverName', 'world'],
      secrets: { password: { set: true } },
      restartRequired: false,
    },
    'valheim-hardcore': {
      settings: { serverName: 'Hardcore weekend', world: 'Niflheim' },
      requiredSettings: ['serverName', 'world'],
      secrets: { password: { set: true } },
      restartRequired: false,
    },
    'valheim-test': {
      settings: {},
      requiredSettings: ['serverName', 'world'],
      secrets: { password: { set: false } },
      restartRequired: false,
    },
  };
}

export const system: SystemInfo = {
  version: '0.2.0-dev',
  startedAt: minutesAgo(3 * 24 * 60 + 245),
  installRoot: 'C:\\Program Files\\GameWarden',
  lastUpdate: {
    from: '0.1.0-rc.2',
    to: '0.2.0-dev',
    outcome: 'succeeded',
    message: 'Now running 0.2.0-dev.',
    at: minutesAgo(3 * 24 * 60 + 246),
  },
  updateCheck: null,
  dataRoot: 'C:\\GameWarden',
  isolation: true,
  runAsUser: 'gamewarden-run',
  vpnRanges: ['26.0.0.0/8', '100.64.0.0/10'],
  machine: { cpuCores: 4, ramTotalMb: 8124, ramFreeMb: 3020, diskFreeMb: 39587 },
  steamCmdInstalled: true,
  network: [
    { host: 'media.steampowered.com', purpose: 'SteamCMD download', status: 'ok', detail: null },
    {
      host: 'steamcdn-a.akamaihd.net',
      purpose: 'SteamCMD download and updates',
      status: 'ok',
      detail: null,
    },
    { host: 'api.github.com', purpose: 'GameWarden updates', status: 'ok', detail: null },
  ],
  prerequisites: [
    {
      id: 'vcredist-x64',
      installed: true,
      hint: 'Microsoft Visual C++ Redistributable 2015-2022 (x64)',
      neededBy: ['valheim'],
    },
    {
      id: 'vcredist-x86',
      installed: false,
      hint: 'Microsoft Visual C++ Redistributable 2015-2022 (x86)',
      neededBy: [],
    },
    {
      id: 'directx-legacy',
      installed: false,
      hint: 'DirectX End-User Runtime (June 2010) from Microsoft',
      neededBy: [],
    },
  ],
  adapterProblems: 0,
};

const valheimLog = [
  'Started valheim_server.exe (pid 7436).',
  "Mono path[0] = 'C:/GameWarden/instances/valheim-main/server/valheim_server_Data/Managed'",
  'Initialize engine version: 2022.3.17f1',
  'Steam game server initialized',
  'Load world: Midgard',
  'Game server connected',
  'Session "Vikings of the North" with join code 482913 and IP 26.14.83.2:2456 is active with 0 player(s)',
  'Got connection SteamID 76561198000000001',
  'Got character ZDOID from Ragnhild : -91233/1',
  'Got connection SteamID 76561198000000002',
  'Got character ZDOID from Bjorn : 77812/3',
  'World saved ( 812.33ms )',
];

export function initialConsole(): Record<string, OutputLine[]> {
  const start = Date.now() - valheimLog.length * 40_000;
  return {
    'valheim-main': valheimLog.map((text, index) => ({
      at: new Date(start + index * 40_000).toISOString(),
      stream: index === 0 ? 'runner' : 'stdout',
      text,
    })),
    'valheim-hardcore': [
      { at: minutesAgo(30), stream: 'runner', text: 'Started valheim_server.exe (pid 6120).' },
      { at: minutesAgo(29), stream: 'stderr', text: 'Exception: failed to load world Niflheim' },
      { at: minutesAgo(29), stream: 'runner', text: 'Game exited with code 3.' },
    ],
  };
}

export const ambientLines = [
  'World saved ( 640.18ms )',
  'Connections 2 ZDOS:31244  sent:12 recv:19',
  'Got character ZDOID from Ragnhild : -91233/1',
  'Sending 4 fragments of 1024 bytes',
];
