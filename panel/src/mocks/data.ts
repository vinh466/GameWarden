import type {
  ActivityItem,
  AdapterSummary,
  InstanceSnapshot,
  JobInfo,
  MetricPoint,
  MetricSeries,
  MetricsReport,
  OutputLine,
  SettingsView,
  SystemInfo,
} from '@/api/generated';
import { valheimGroups, valheimSettings } from './valheimSettings';

// Mock state for `npm run dev:mock` and component tests: shaped like Core's answers (generated types), invented values.
const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

/** A plausible next sample: a slow wave plus noise, so the sparklines move in `dev:mock`. */
export function metricPoint(at: Date, base: { cpu: number; ramMb: number }): MetricPoint {
  const t = at.getTime() / 60_000;
  return {
    at: at.toISOString(),
    cpuPercent: Math.max(
      0,
      Math.round((base.cpu + 8 * Math.sin(t / 2) + Math.random() * 6) * 10) / 10
    ),
    ramMb: Math.round(base.ramMb + 150 * Math.sin(t / 3) + Math.random() * 40),
  };
}

export const metricBases: Record<string, { cpu: number; ramMb: number }> = {
  machine: { cpu: 24, ramMb: 5100 },
  'valheim-main': { cpu: 14, ramMb: 2350 },
};

/** Ten minutes of 5 s samples for the machine and the running server. */
export function initialMetrics(): MetricsReport {
  const series = (base: { cpu: number; ramMb: number }): MetricSeries => {
    const history = Array.from({ length: 120 }, (_, i) =>
      metricPoint(new Date(Date.now() - (119 - i) * 5_000), base)
    );
    return { latest: history.at(-1) ?? null, history };
  };
  return {
    machine: series(metricBases.machine!),
    instances: { 'valheim-main': series(metricBases['valheim-main']!) },
  };
}

/** Newest first, as Core returns it. */
export function initialActivity(): ActivityItem[] {
  const item = (
    id: number,
    minutes: number,
    kind: string,
    message: string,
    instanceId: string | null,
    level = 'info',
    user: string | null = null
  ): ActivityItem => ({
    id,
    at: minutesAgo(minutes),
    kind,
    level,
    instanceId,
    jobId: null,
    user,
    message,
  });
  return [
    item(9, 2, 'command', 'Install started.', 'valheim-test', 'info', 'owner'),
    item(8, 3, 'instance.created', 'Created Test world.', 'valheim-test'),
    item(7, 29, 'instance.crashed', 'Crashed: exited with code 3.', 'valheim-hardcore', 'error'),
    item(6, 30, 'command', 'Start requested.', 'valheim-hardcore', 'info', 'owner'),
    item(5, 376, 'instance.running', 'Running.', 'valheim-main'),
    item(
      4,
      377,
      'job.succeeded',
      'Game update: Installed; the server is starting again.',
      'valheim-main'
    ),
    item(3, 380, 'command', 'Game update started.', 'valheim-main', 'info', 'cli'),
    item(
      2,
      797,
      'job.failed',
      "Install: install failed: ERROR! Failed to install app '896660' (No connection)",
      'valheim-hardcore',
      'error'
    ),
    item(1, 3 * 24 * 60 + 245, 'core.started', 'GameWarden 0.2.0-dev started.', null),
  ];
}

export const adapters: AdapterSummary[] = [
  {
    id: 'valheim',
    name: 'Valheim',
    version: '0.3.2',
    level: 'L2',
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
    settingGroups: valheimGroups,
    settings: valheimSettings,
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
      settings: {
        serverName: 'Hardcore weekend',
        world: 'Niflheim',
        preset: 'hardcore',
        raids: 'more',
        noMap: 'true',
      },
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
  updateCheck: {
    current: '0.2.0-dev',
    latest: '0.2.0',
    available: true,
    url: 'https://github.com/vinh466/GameWarden/releases/tag/v0.2.0',
  },
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
