import type {
  ActivityItem,
  AdapterSummary,
  AgentAuditView,
  AgentClientView,
  AgentPermissionCatalog,
  AgentSettingsView,
  AgentTokenView,
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
    item(1, 3 * 24 * 60 + 245, 'core.started', 'GameWarden 0.3.0-dev.8 started.', null),
  ];
}

export const adapters: AdapterSummary[] = [
  {
    id: 'valheim',
    name: 'Valheim',
    version: '0.5.1',
    level: 'L3',
    modLoader: 'bepinex',
    integration: {
      provider: 'valheim-console',
      label: { en: 'Game API', vi: 'Game API' },
    },
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
      launchSummary:
        'C:\\GameWarden\\instances\\valheim-main\\server\\valheim_server.exe -nographics -batchmode -name "Vikings of the North" -port 2456 -world Midgard -password <secret>',
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
      launchSummary:
        'C:\\GameWarden\\instances\\valheim-hardcore\\server\\valheim_server.exe -nographics -batchmode -name "Hardcore weekend" -port 2466 -world Hardcore -password <secret>',
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
      launchSummary:
        'C:\\GameWarden\\instances\\valheim-test\\server\\valheim_server.exe -nographics -batchmode -name "Test world" -port 2476 -world Test -password <secret>',
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
  version: '0.3.0-dev.8',
  startedAt: minutesAgo(3 * 24 * 60 + 245),
  installRoot: 'C:\\Program Files\\GameWarden',
  lastUpdate: {
    from: '0.3.0-dev.7',
    to: '0.3.0-dev.8',
    outcome: 'succeeded',
    message: 'Now running 0.3.0-dev.8.',
    at: minutesAgo(3 * 24 * 60 + 246),
  },
  updateCheck: {
    current: '0.3.0-dev.8',
    latest: '0.3.0-dev.9',
    available: true,
    url: 'https://github.com/vinh466/GameWarden/releases/tag/v0.3.0-dev.9',
    notes:
      '## Data impact\n\n- Update only: instances and the database stay as they are.\n- Back up `C:\\GameWarden\\instances` and `C:\\GameWarden\\db` if you want a safety net.',
    publishedAt: minutesAgo(60),
    checkedAt: minutesAgo(2),
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

// Agent access (docs/15): one development client with a small scope, its tokens and a short audit page.
export const agentSettings: AgentSettingsView = {
  enabled: true,
  requireAdminMode: false,
  diagnostics: { enabled: false, expiresAt: null, reason: null },
  admin: { enabled: false, expiresAt: null, reason: null },
  allowedNetworks: ['100.64.0.0/10'],
  updatedAt: minutesAgo(60),
};

export const agentClients: AgentClientView[] = [
  {
    id: 'opencode-devbox',
    name: 'OpenCode devbox',
    description: 'Development Agent on the Linux box.',
    enabled: true,
    trustedFull: false,
    permissions: [
      'agent.status.read',
      'agent.instances.read',
      'agent.jobs.read',
      'agent.activity.read',
      'agent.logs.read',
      'agent.metrics.read',
    ],
    allowedNetworks: [],
    createdAt: minutesAgo(60 * 24 * 12),
    updatedAt: minutesAgo(60),
    lastSeenAt: minutesAgo(4),
    lastSeenIp: '100.64.0.10',
    lastSeenVersion: '0.3.0-dev.8',
  },
];

const observePermissions = [
  'agent.status.read',
  'agent.instances.read',
  'agent.jobs.read',
  'agent.activity.read',
  'agent.logs.read',
  'agent.metrics.read',
  'agent.settings.read',
  'agent.mods.read',
];
const operatePermissions = [
  ...observePermissions,
  'agent.instances.control',
  'agent.instances.console.write',
  'agent.instances.install',
  'agent.instances.update',
];
const diagnosePermissions = [
  ...observePermissions,
  'agent.diagnostics.system',
  'agent.diagnostics.processes',
  'agent.diagnostics.network',
  'agent.diagnostics.storage',
  'agent.diagnostics.events',
  'agent.diagnostics.files',
  'agent.diagnostics.bundles',
  'agent.runbooks.read',
  'agent.runbooks.run',
];
const adminPermissions = [
  'agent.instances.delete',
  'agent.backups.restore',
  'agent.system.update',
  'agent.system.settings',
  'agent.adapters.manage',
];

export const agentPermissionCatalog: AgentPermissionCatalog = {
  permissions: [
    {
      key: 'agent.status.read',
      group: 'observe',
      description: 'Read the Agent status summary.',
      risk: 'low',
    },
    {
      key: 'agent.instances.read',
      group: 'observe',
      description: 'Read instances and their console tail.',
      risk: 'low',
    },
    {
      key: 'agent.jobs.read',
      group: 'observe',
      description: 'Read jobs and job logs.',
      risk: 'low',
    },
    {
      key: 'agent.activity.read',
      group: 'observe',
      description: 'Read the activity feed.',
      risk: 'low',
    },
    {
      key: 'agent.logs.read',
      group: 'observe',
      description: 'Read instance and job logs.',
      risk: 'medium',
    },
    {
      key: 'agent.metrics.read',
      group: 'observe',
      description: 'Read CPU and RAM metrics.',
      risk: 'low',
    },
    {
      key: 'agent.settings.read',
      group: 'observe',
      description: 'Read instance settings; secrets stay masked.',
      risk: 'medium',
    },
    {
      key: 'agent.mods.read',
      group: 'observe',
      description: 'Read mod sets, logs and configs.',
      risk: 'medium',
    },
    {
      key: 'agent.instances.control',
      group: 'operate',
      description: 'Start, stop and restart game servers.',
      risk: 'medium',
    },
    {
      key: 'agent.instances.console.write',
      group: 'operate',
      description: 'Send allow-listed console commands.',
      risk: 'medium',
    },
    {
      key: 'agent.instances.install',
      group: 'operate',
      description: 'Install game files with SteamCMD.',
      risk: 'medium',
    },
    {
      key: 'agent.instances.update',
      group: 'operate',
      description: 'Update game files with SteamCMD.',
      risk: 'medium',
    },
    {
      key: 'agent.instances.settings.write',
      group: 'write',
      description: 'Change instance settings.',
      risk: 'high',
    },
    {
      key: 'agent.instances.mods.write',
      group: 'write',
      description: 'Change mod sets and mod configs; mods are code.',
      risk: 'high',
    },
    {
      key: 'agent.instances.config.write',
      group: 'write',
      description: 'Write rendered config files.',
      risk: 'high',
    },
    {
      key: 'agent.diagnostics.system',
      group: 'diagnose',
      description: 'Read OS, service, disk and prerequisite diagnostics.',
      risk: 'high',
    },
    {
      key: 'agent.diagnostics.processes',
      group: 'diagnose',
      description: 'Read processes and redacted command lines.',
      risk: 'high',
    },
    {
      key: 'agent.diagnostics.network',
      group: 'diagnose',
      description: 'Read interfaces, listeners, connections and firewall rules.',
      risk: 'high',
    },
    {
      key: 'agent.diagnostics.storage',
      group: 'diagnose',
      description: 'Read volumes, folder sizes and save inventory.',
      risk: 'high',
    },
    {
      key: 'agent.diagnostics.events',
      group: 'diagnose',
      description: 'Read Windows Event Log entries.',
      risk: 'high',
    },
    {
      key: 'agent.diagnostics.files',
      group: 'diagnose',
      description: 'Read allow-listed instance files, redacted.',
      risk: 'high',
    },
    {
      key: 'agent.diagnostics.bundles',
      group: 'diagnose',
      description: 'Create and download redacted diagnostic bundles.',
      risk: 'high',
    },
    {
      key: 'agent.runbooks.read',
      group: 'diagnose',
      description: 'List diagnostic runbooks.',
      risk: 'low',
    },
    {
      key: 'agent.runbooks.run',
      group: 'diagnose',
      description: 'Run diagnostic runbooks.',
      risk: 'high',
    },
    {
      key: 'agent.instances.delete',
      group: 'admin',
      description: 'Delete an instance and its files.',
      risk: 'critical',
    },
    {
      key: 'agent.backups.restore',
      group: 'admin',
      description: 'Restore a backup.',
      risk: 'critical',
    },
    {
      key: 'agent.system.update',
      group: 'admin',
      description: 'Update GameWarden itself.',
      risk: 'critical',
    },
    {
      key: 'agent.system.settings',
      group: 'admin',
      description: 'Change panel, VPN and Agent settings.',
      risk: 'critical',
    },
    {
      key: 'agent.adapters.manage',
      group: 'admin',
      description: 'Reload or change adapters; adapters are code.',
      risk: 'critical',
    },
  ],
  presets: [
    {
      key: 'observer',
      name: 'Observer',
      description: 'Read status, instances, jobs, activity and logs.',
      permissions: observePermissions,
    },
    {
      key: 'operator',
      name: 'Operator',
      description: 'Observe and run the servers: start, stop, restart, install, update, console.',
      permissions: operatePermissions,
    },
    {
      key: 'debugger',
      name: 'Debugger',
      description: 'Observe plus deep diagnostics and runbooks.',
      permissions: diagnosePermissions,
    },
    {
      key: 'full',
      name: 'Full',
      description: 'Every permission that exists now.',
      permissions: [
        ...observePermissions,
        ...operatePermissions.slice(observePermissions.length),
        ...diagnosePermissions.slice(observePermissions.length),
        ...adminPermissions,
      ],
    },
  ],
};

export const agentTokens: AgentTokenView[] = [
  {
    id: 'tok-devbox',
    name: 'panel',
    prefix: 'gw_agent_7f3a',
    createdAt: minutesAgo(60 * 24 * 12),
    expiresAt: new Date(Date.now() + 18 * 24 * 60 * 60_000).toISOString(),
    revokedAt: null,
    lastUsedAt: minutesAgo(4),
    lastUsedIp: '100.64.0.10',
  },
];

export const agentAudit: AgentAuditView[] = [
  {
    id: 4,
    at: minutesAgo(4),
    clientId: 'opencode-devbox',
    clientName: 'OpenCode devbox',
    tokenId: 'tok-devbox',
    action: 'GET /api/v1/agent/status',
    target: null,
    outcome: 'ok',
    statusCode: 200,
    sourceIp: '100.64.0.10',
    requestId: 'req-4',
    durationMs: 18,
    details: null,
  },
  {
    id: 3,
    at: minutesAgo(31),
    clientId: 'opencode-devbox',
    clientName: 'OpenCode devbox',
    tokenId: 'tok-devbox',
    action: 'POST /api/v1/agent/instances/valheim-main/start',
    target: 'valheim-main',
    outcome: 'denied',
    statusCode: 403,
    sourceIp: '100.64.0.10',
    requestId: 'req-3',
    durationMs: 3,
    details: 'Missing scope instance.control.',
  },
  {
    id: 2,
    at: minutesAgo(60 * 5),
    clientId: 'opencode-devbox',
    clientName: 'OpenCode devbox',
    tokenId: 'tok-devbox',
    action: 'GET /api/v1/agent/logs',
    target: null,
    outcome: 'ok',
    statusCode: 200,
    sourceIp: '100.64.0.10',
    requestId: 'req-2',
    durationMs: 41,
    details: null,
  },
];
