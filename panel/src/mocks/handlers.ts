import { http, HttpResponse } from 'msw/http';
import type {
  ActualState,
  AgentTokenCreated,
  CreateAgentClientRequest,
  CreateAgentTokenRequest,
  CreateInstanceRequest,
  GameIntegrationView,
  InstanceSnapshot,
  JobInfo,
  NotificationDestinationView,
  SettingsUpdate,
  SystemSettingsUpdate,
  SystemSettingsView,
  UpdateAgentClientRequest,
  UpdateAgentSettingsRequest,
  UpdateProgress,
} from '@/api/generated';
import { addMetricsSample } from '@/api/realtimeCache';
import {
  adapters,
  agentAudit,
  agentClients,
  agentPermissionCatalog,
  agentSettings,
  agentTokens,
  ambientLines,
  initialActivity,
  initialConsole,
  initialMetrics,
  metricBases,
  metricPoint,
  initialInstances,
  initialJobs,
  initialSettings,
  system,
} from './data';
import { modsHandlers } from './mods';
import { valheimHelp } from './valheimHelp';

// A small in-memory Core for `npm run dev:mock` (docs/11 §14 phase 1): commands move states the way Core does, jobs make
// progress, a running server prints now and then. Routes and bodies follow the generated types.
let updateProgress: UpdateProgress = {
  phase: 'idle',
  percent: null,
  message: 'No update is running.',
  startedAt: null,
  finishedAt: null,
  error: null,
};

const db = {
  instances: initialInstances(),
  jobs: initialJobs(),
  activity: initialActivity(),
  metrics: initialMetrics(),
  settingsView: {
    panelAccessNetworks: ['26.0.0.0/8', '100.64.0.0/10'],
    panelAccessNetworksDefault: true,
    vpnRanges: ['26.0.0.0/8', '100.64.0.0/10'],
    vpnRangesDefault: true,
    defaultVpnRanges: ['26.0.0.0/8', '100.64.0.0/10'],
  } as SystemSettingsView,
  settings: initialSettings(),
  console: initialConsole(),
};

const problem = (status: number, code: string, title: string) =>
  HttpResponse.json(
    { status, title, code },
    { status, headers: { 'Content-Type': 'application/problem+json' } }
  );

const agentHostSettings = {
  schemaVersion: 1,
  enabled: false,
  model: {
    baseUrl: 'https://model.example/v1',
    model: 'deepseek-v4.1-flash',
    maxTokens: 4096,
    temperature: 0.2,
    maxToolCalls: 12,
    timeoutSeconds: 120,
    headers: {},
  },
  discord: {
    guildId: '123456789012345678',
    channels: {
      'valheim-main': '111111111111111111',
      system: '222222222222222222',
    },
    allowedUserIds: [],
    allowedRoleIds: [],
    requireMention: true,
    language: 'vi',
  },
  autonomy: {
    mode: 'approve-writes',
    approvalTimeoutMinutes: 15,
    maxWritesPerHour: 6,
  },
};

let agentHostModelKeySet = false;
let agentHostBotTokenSet = false;

const agentHostView = () => ({
  enabled: agentHostSettings.enabled,
  modelConfigured: true,
  modelKeySet: agentHostModelKeySet,
  botTokenSet: agentHostBotTokenSet,
  botConnected: false,
  lastError: null,
  settings: agentHostSettings,
});

const notificationCatalog = [
  { key: 'instance.crashed', category: 'instance', defaultSeverity: 'error' },
  { key: 'instance.faulted', category: 'instance', defaultSeverity: 'critical' },
  { key: 'job.failed', category: 'job', defaultSeverity: 'error' },
  { key: 'update.available', category: 'update', defaultSeverity: 'warning' },
  { key: 'core.recovered', category: 'system', defaultSeverity: 'warning' },
  { key: 'player.joined', category: 'player', defaultSeverity: 'info', digest: true },
];

const notificationDestinations: NotificationDestinationView[] = [
  {
    id: 'discord-owner',
    name: 'Owner Discord',
    channel: 'discord-webhook',
    enabled: true,
    mode: 'instant',
    language: 'vi',
    minimumSeverity: 'warning',
    topics: ['*'],
    instances: ['*'],
    mentionRoleId: null,
    panelUrl: null,
    intervalSec: 60,
    digestSec: 300,
    webhookSet: true,
  },
  {
    id: 'discord-status',
    name: 'Status board',
    channel: 'discord-webhook',
    enabled: true,
    mode: 'status',
    language: 'vi',
    minimumSeverity: 'info',
    topics: ['*'],
    instances: ['*'],
    mentionRoleId: null,
    panelUrl: null,
    intervalSec: 300,
    digestSec: 300,
    webhookSet: true,
  },
];

const find = (id: string) => db.instances.find((instance) => instance.id === id);

function setState(id: string, state: ActualState, changes: Partial<InstanceSnapshot> = {}) {
  db.instances = db.instances.map((instance) =>
    instance.id === id ? { ...instance, ...changes, state } : instance
  );
}

function log(id: string, text: string, stream: 'stdout' | 'stderr' | 'runner' = 'stdout') {
  (db.console[id] ??= []).push({ at: new Date().toISOString(), stream, text });
}

function after(ms: number, action: () => void) {
  setTimeout(action, ms);
}

function startJob(instance: InstanceSnapshot, type: 'install' | 'update'): JobInfo {
  const job: JobInfo = {
    id: `${new Date().toISOString().replace(/\D/g, '').slice(0, 8)}-${Math.random().toString(16).slice(2, 8)}`,
    type,
    instanceId: instance.id,
    state: 'running',
    stage: 'steamcmd.prepare',
    progress: null,
    message: null,
    createdAt: new Date().toISOString(),
    finishedAt: null,
  };
  db.jobs.unshift(job);
  setState(instance.id, instance.state, { activeJobId: job.id });
  return job;
}

// Running jobs advance; running servers print a line now and then.
setInterval(() => {
  db.jobs = db.jobs.map((job) => {
    // Mods jobs move on their own timers (./mods).
    if (job.state !== 'running' || job.type === 'mods.sync') {
      return job;
    }

    const progress = Math.min(100, (job.progress ?? 0) + 7 + Math.random() * 8);
    if (progress < 100) {
      return {
        ...job,
        stage: job.type === 'install' ? 'steamcmd.install' : 'steamcmd.update',
        progress,
      };
    }

    const instance = job.instanceId ? find(job.instanceId) : undefined;
    if (instance) {
      setState(instance.id, instance.state === 'notInstalled' ? 'stopped' : instance.state, {
        activeJobId: null,
      });
    }

    return {
      ...job,
      state: 'succeeded',
      stage: 'start',
      progress: 100,
      message: 'Installed.',
      finishedAt: new Date().toISOString(),
    };
  });

  for (const instance of db.instances.filter((candidate) => candidate.state === 'running')) {
    if (Math.random() < 0.3) {
      log(instance.id, ambientLines[Math.floor(Math.random() * ambientLines.length)] ?? '');
    }
  }
}, 1500);

export const handlers = [
  http.get('/healthz', () => HttpResponse.json({ status: 'ok' })),
  ...modsHandlers({
    find,
    addJob: (job) => db.jobs.unshift(job),
    updateJob: (id, changes) => {
      db.jobs = db.jobs.map((job) => (job.id === id ? { ...job, ...changes } : job));
    },
  }),

  http.get('/api/v1/system', () => HttpResponse.json(system)),
  http.get('/api/v1/system/logs', ({ request }) => {
    const url = new URL(request.url);
    const level = url.searchParams.get('level');
    const component = url.searchParams.get('component');
    const instanceId = url.searchParams.get('instanceId');
    const jobId = url.searchParams.get('jobId');
    const requestId = url.searchParams.get('requestId');
    const search = url.searchParams.get('q')?.toLowerCase();
    const items = [
      {
        id: 3,
        at: new Date().toISOString(),
        level: 'info',
        component: 'GameWarden.Core.Supervision.Supervisor',
        category: 'GameWarden.Core.Supervision.Supervisor',
        message: 'Supervising 2 instance(s).',
        exception: null,
        instanceId: null,
        jobId: null,
        requestId: null,
        agentClientId: null,
        source: 'core',
        data: null,
      },
      {
        id: 2,
        at: new Date(Date.now() - 60_000).toISOString(),
        level: 'warn',
        component: 'GameWarden.Core.Metrics.MetricsSampler',
        category: 'GameWarden.Core.Metrics.MetricsSampler',
        message: 'Sampling CPU and RAM failed.',
        exception: 'Win32Exception: Access denied',
        instanceId: 'valheim-main',
        jobId: null,
        requestId: null,
        agentClientId: null,
        source: 'core',
        data: '{"stage":"sample"}',
      },
      {
        id: 1,
        at: new Date(Date.now() - 120_000).toISOString(),
        level: 'error',
        component: 'GameWarden.Core.Jobs.JobService',
        category: 'GameWarden.Core.Jobs.JobService',
        message: 'Job 20261103-101500-ab12 failed: SteamCMD exited 8.',
        exception: null,
        instanceId: 'valheim-main',
        jobId: '20261103-101500-ab12',
        requestId: null,
        agentClientId: null,
        source: 'core',
        data: null,
      },
    ];
    const filtered = items.filter(
      (item) =>
        (!level || item.level === level) &&
        (!component || item.component === component) &&
        (!instanceId || item.instanceId === instanceId) &&
        (!jobId || item.jobId === jobId) &&
        (!requestId || item.requestId === requestId) &&
        (!search || item.message.toLowerCase().includes(search))
    );
    const limit = Number(url.searchParams.get('limit') ?? 100);
    return HttpResponse.json({
      items: filtered.slice(0, limit),
      nextBefore: filtered.length > limit ? (filtered[limit - 1]?.id ?? null) : null,
    });
  }),
  http.get('/api/v1/system/logs/summary', () =>
    HttpResponse.json({
      since: new Date(Date.now() - 3_600_000).toISOString(),
      total: 3,
      levels: [
        { key: 'info', count: 1 },
        { key: 'warn', count: 1 },
        { key: 'error', count: 1 },
      ],
      components: [
        { key: 'GameWarden.Core.Supervision.Supervisor', count: 1 },
        { key: 'GameWarden.Core.Metrics.MetricsSampler', count: 1 },
        { key: 'GameWarden.Core.Jobs.JobService', count: 1 },
      ],
      topMessages: [],
      newestError: null,
      newestCritical: null,
    })
  ),
  http.get('/api/v1/system/logs/components', () =>
    HttpResponse.json([
      {
        component: 'GameWarden.Core.Supervision.Supervisor',
        count: 1,
        lastAt: new Date().toISOString(),
      },
      {
        component: 'GameWarden.Core.Metrics.MetricsSampler',
        count: 1,
        lastAt: new Date().toISOString(),
      },
      { component: 'GameWarden.Core.Jobs.JobService', count: 1, lastAt: new Date().toISOString() },
    ])
  ),
  http.get('/api/v1/system/notifications', () =>
    HttpResponse.json({
      destinations: notificationDestinations,
      catalog: notificationCatalog,
      supportedModes: ['instant', 'digest', 'status'],
    })
  ),
  http.put('/api/v1/system/notifications', async ({ request }) => {
    const body = (await request.json()) as {
      destinations?: Array<NotificationDestinationView>;
    };
    const next = (body.destinations ?? []).map((destination) => ({
      ...destination,
      webhookSet: destination.webhookSet ?? true,
    }));
    notificationDestinations.splice(0, notificationDestinations.length, ...next);
    return HttpResponse.json({
      destinations: notificationDestinations,
      catalog: notificationCatalog,
      supportedModes: ['instant', 'digest', 'status'],
    });
  }),
  http.post('/api/v1/system/notifications/destinations/:id/test', () =>
    HttpResponse.json({ ok: true, messageId: 'mock-1' })
  ),
  http.get('/api/v1/system/notifications/status-preview', () =>
    HttpResponse.json({
      text: db.instances
        .map((instance) => `${instance.displayName} · ${instance.id}  ${instance.state}`)
        .join('\n'),
    })
  ),
  http.get('/api/v1/system/notifications/deliveries', () =>
    HttpResponse.json([
      {
        id: 1,
        eventId: 1,
        destinationId: 'discord-owner',
        topic: 'job.failed',
        severity: 'error',
        title: 'Install failed on Hardcore weekend',
        state: 'failed',
        attempts: 3,
        lastError: 'Discord returned 429 (rate limited).',
        messageId: null,
        at: new Date(Date.now() - 13 * 60_000).toISOString(),
        nextAttemptAt: null,
        sentAt: null,
      },
      {
        id: 2,
        eventId: 2,
        destinationId: 'discord-owner',
        topic: 'instance.crashed',
        severity: 'error',
        title: 'Hardcore weekend crashed (exit 3)',
        state: 'sent',
        attempts: 1,
        lastError: null,
        messageId: 'mock-2',
        at: new Date(Date.now() - 31 * 60_000).toISOString(),
        nextAttemptAt: null,
        sentAt: new Date(Date.now() - 31 * 60_000).toISOString(),
      },
    ])
  ),
  http.post('/api/v1/system/notifications/deliveries/:id/retry', ({ params }) =>
    HttpResponse.json({ id: Number(params.id), retried: true })
  ),
  http.post('/api/v1/system/restart-core', () => new HttpResponse(null, { status: 202 })),
  http.get('/api/v1/system/agent-host', () => HttpResponse.json(agentHostView())),
  http.get('/api/v1/system/agent-host/incidents', () => HttpResponse.json([])),
  http.get('/api/v1/system/agent-host/usage', () =>
    HttpResponse.json({
      turns: 12,
      modelCalls: 18,
      modelErrors: 1,
      promptTokens: 12450,
      completionTokens: 2180,
      avgLatencyMs: 1840,
      maxLatencyMs: 4200,
      pendingApprovals: 1,
      openIncidents: 1,
      recentCalls: [
        {
          id: 3,
          at: new Date().toISOString(),
          purpose: 'chat',
          model: 'deepseek-v4.1-flash',
          latencyMs: 1260,
          promptTokens: 820,
          completionTokens: 140,
          toolCalls: 1,
          status: 'ok',
          traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
          promptPreview:
            'System: Bạn là GameWarden Operations Agent…\nUser: các mod đang active là gì',
          responsePreview: '{"content":"Có 18 mod đang bật…","toolCalls":[]}',
        },
        {
          id: 2,
          at: new Date(Date.now() - 60000).toISOString(),
          purpose: 'verification',
          model: 'deepseek-v4.1-flash',
          latencyMs: 4200,
          promptTokens: 940,
          completionTokens: 180,
          toolCalls: 2,
          status: 'ok',
          traceId: '9f1c2b3577b34da6a3ce929d0e0e4736',
          promptPreview: 'Approval executed: control_instance restart…',
          responsePreview: 'Đã kiểm tra: server đang Running, health ok.',
        },
        {
          id: 1,
          at: new Date(Date.now() - 120000).toISOString(),
          purpose: 'runbook.summarize',
          model: 'deepseek-v4.1-flash',
          latencyMs: 1900,
          promptTokens: 1100,
          completionTokens: 210,
          toolCalls: 0,
          status: 'error',
          error: 'Model endpoint answered HTTP 400.',
          traceId: '1a2c3b3577b34da6a3ce929d0e0e4736',
          promptPreview: 'Task: Chẩn đoán crash…',
          responsePreview: null,
        },
      ],
    })
  ),
  http.get('/api/v1/system/agent-host/incidents/:id', ({ params }) =>
    HttpResponse.json({
      incident: { id: params.id, state: 'open', title: 'Mock incident' },
      timeline: [],
    })
  ),
  http.post('/api/v1/system/agent-host/incidents/:id/diagnose', () =>
    HttpResponse.json({ summary: 'Mock diagnosis' })
  ),
  http.put('/api/v1/system/agent-host', async ({ request }) => {
    const body = (await request.json()) as typeof agentHostSettings;
    Object.assign(agentHostSettings, body);
    return HttpResponse.json(agentHostView());
  }),
  http.post('/api/v1/system/agent-host/test-model', () =>
    HttpResponse.json({
      ok: true,
      checks: [
        { name: 'Base URL', ok: true, detail: agentHostSettings.model.baseUrl },
        { name: 'Model id', ok: true, detail: agentHostSettings.model.model },
        { name: 'API key', ok: true, detail: 'set' },
        { name: 'Request', ok: true, detail: 'HTTP 200' },
        { name: 'Response', ok: true, detail: 'OK' },
      ],
      latencyMs: 42,
    })
  ),
  http.post('/api/v1/system/agent-host/test-discord', () =>
    HttpResponse.json({
      ok: true,
      checks: [
        { name: 'Bot token', ok: true, detail: 'set' },
        { name: 'Guild', ok: true, detail: agentHostSettings.discord.guildId },
        { name: 'Channel id', ok: true, detail: '222222222222222222' },
        { name: 'Discord login', ok: true, detail: 'GameWarden' },
        { name: 'Channel', ok: true, detail: '#system' },
        { name: 'Message', ok: true, detail: '#system (id 1)' },
      ],
      latencyMs: 15,
    })
  ),
  http.put('/api/v1/system/agent-host/secrets/:name', ({ params }) => {
    if (String(params.name) === 'model-api-key') {
      agentHostModelKeySet = true;
    }

    if (String(params.name) === 'discord-bot-token') {
      agentHostBotTokenSet = true;
    }

    return HttpResponse.json(agentHostView());
  }),
  http.get('/api/v1/system/settings', () => HttpResponse.json(db.settingsView)),
  http.put('/api/v1/system/settings', async ({ request }) => {
    const body = (await request.json()) as SystemSettingsUpdate;
    const panel = (body.panelAccessNetworks ?? []).filter(Boolean);
    const vpn = (body.vpnRanges ?? []).filter(Boolean);
    const bad = [...panel, ...vpn].find((range) => !/^[\d.:a-f]+\/\d{1,3}$/i.test(range));
    if (bad) {
      return problem(400, 'validation_failed', `'${bad}' is not a range like 26.0.0.0/8.`);
    }

    const defaults = ['26.0.0.0/8', '100.64.0.0/10'];
    db.settingsView = {
      vpnRanges: vpn.length ? vpn : defaults,
      vpnRangesDefault: vpn.length === 0,
      panelAccessNetworks: panel.length ? panel : vpn.length ? vpn : defaults,
      panelAccessNetworksDefault: panel.length === 0,
      defaultVpnRanges: defaults,
    };
    return HttpResponse.json(db.settingsView);
  }),
  http.get('/api/v1/system/update', () => HttpResponse.json(system.updateCheck)),
  http.get('/api/v1/system/update/status', () => HttpResponse.json(updateProgress)),
  http.post('/api/v1/system/update', async ({ request }) => {
    const body = (await request.json()) as { version?: string | null };
    const from = system.version;
    const to = body.version ?? system.updateCheck?.latest ?? '0.3.0-dev.9';
    updateProgress = {
      phase: 'restarting',
      percent: 100,
      message: `Restarting into ${to}.`,
      startedAt: new Date().toISOString(),
      finishedAt: null,
      error: null,
    };
    window.setTimeout(() => {
      system.version = to;
      system.startedAt = new Date().toISOString();
      system.lastUpdate = {
        from,
        to,
        outcome: 'succeeded',
        message: `Now running ${to}.`,
        at: new Date().toISOString(),
      };
      if (system.updateCheck) {
        system.updateCheck = { ...system.updateCheck, current: to, available: false, latest: to };
      }
      updateProgress = {
        ...updateProgress,
        phase: 'idle',
        percent: null,
        message: 'No update is running.',
        startedAt: null,
        finishedAt: new Date().toISOString(),
      };
    }, 1_500);
    return HttpResponse.json({ from, to, message: `Updating to ${to}.` }, { status: 202 });
  }),

  http.get('/api/v1/adapters', () => HttpResponse.json({ adapters, problems: [] })),
  http.get('/api/v1/adapters/:id/help', ({ params }) => {
    const id = String(params.id);
    if (!adapters.some((adapter) => adapter.id === id)) {
      return problem(404, 'not_found', `No adapter '${id}'.`);
    }
    return HttpResponse.json(id === 'valheim' ? valheimHelp : { topics: {} });
  }),
  http.post('/api/v1/adapters/reload', () =>
    HttpResponse.json({ adapters: adapters.length, problems: [] })
  ),

  http.get('/api/v1/agents', () => HttpResponse.json(agentClients)),
  http.post('/api/v1/agents', async ({ request }) => {
    const body = (await request.json()) as CreateAgentClientRequest;
    const id =
      (body.name ?? 'agent')
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-+|-+$/g, '') || `agent-${agentClients.length + 1}`;
    const now = new Date().toISOString();
    const client = {
      id,
      name: body.name ?? id,
      description: body.description,
      enabled: true,
      trustedFull: body.trustedFull ?? false,
      permissions: body.permissions ?? [],
      allowedNetworks: body.allowedNetworks ?? [],
      createdAt: now,
      updatedAt: now,
      lastSeenAt: null,
      lastSeenIp: null,
      lastSeenVersion: null,
    };
    agentClients.push(client);
    return HttpResponse.json(client, { status: 201 });
  }),
  http.get('/api/v1/agents/permissions', () => HttpResponse.json(agentPermissionCatalog)),
  http.get('/api/v1/agents/settings', () => HttpResponse.json(agentSettings)),
  http.put('/api/v1/agents/settings', async ({ request }) => {
    const body = (await request.json()) as UpdateAgentSettingsRequest;
    if (body.enabled !== null) {
      agentSettings.enabled = body.enabled;
    }
    if (body.requireAdminMode !== null) {
      agentSettings.requireAdminMode = body.requireAdminMode;
    }
    if (body.diagnostics) {
      agentSettings.diagnostics = {
        enabled: body.diagnostics.enabled,
        expiresAt: body.diagnostics.enabled
          ? new Date(Date.now() + (body.diagnostics.ttlMinutes ?? 30) * 60_000).toISOString()
          : null,
        reason: body.diagnostics.reason,
      };
    }
    if (body.admin) {
      agentSettings.admin = {
        enabled: body.admin.enabled,
        expiresAt: body.admin.enabled
          ? new Date(Date.now() + (body.admin.ttlMinutes ?? 30) * 60_000).toISOString()
          : null,
        reason: body.admin.reason,
      };
    }
    agentSettings.updatedAt = new Date().toISOString();
    return HttpResponse.json(agentSettings);
  }),
  http.get('/api/v1/agents/audit', () => HttpResponse.json(agentAudit)),
  http.patch('/api/v1/agents/:id', async ({ params, request }) => {
    const client = agentClients.find((candidate) => candidate.id === String(params.id));
    if (!client) {
      return problem(404, 'not_found', `No Agent client '${String(params.id)}'.`);
    }
    const body = (await request.json()) as UpdateAgentClientRequest;
    if (body.name !== null) {
      client.name = body.name;
    }
    if (body.description !== null) {
      client.description = body.description;
    }
    if (body.enabled !== null) {
      client.enabled = body.enabled;
    }
    if (body.permissions !== null) {
      client.permissions = body.permissions;
    }
    if (body.trustedFull !== null) {
      client.trustedFull = body.trustedFull;
    }
    if (body.allowedNetworks !== null) {
      client.allowedNetworks = body.allowedNetworks;
    }
    client.updatedAt = new Date().toISOString();
    return HttpResponse.json(client);
  }),
  http.get('/api/v1/agents/:id/tokens', () => HttpResponse.json(agentTokens)),
  http.post('/api/v1/agents/:id/tokens', async ({ request }) => {
    const body = (await request.json()) as CreateAgentTokenRequest;
    const now = new Date();
    const tokenInfo = {
      id: `tok-${agentTokens.length + 1}`,
      name: body.name ?? 'panel',
      prefix: `gw_agent_${Math.random().toString(16).slice(2, 6)}`,
      createdAt: now.toISOString(),
      expiresAt: new Date(
        now.getTime() + (body.expiresInDays ?? 30) * 24 * 60 * 60_000
      ).toISOString(),
      revokedAt: null,
      lastUsedAt: null,
      lastUsedIp: null,
    };
    agentTokens.unshift(tokenInfo);
    const created: AgentTokenCreated = {
      token: `${tokenInfo.prefix}_${Math.random().toString(36).slice(2, 12)}`,
      tokenInfo,
    };
    return HttpResponse.json(created, { status: 201 });
  }),
  http.delete('/api/v1/agents/:id/tokens/:tokenId', ({ params }) => {
    const token = agentTokens.find((candidate) => candidate.id === String(params.tokenId));
    if (token) {
      token.revokedAt = new Date().toISOString();
    }
    return new HttpResponse(null, { status: 204 });
  }),

  http.get('/api/v1/instances', () => HttpResponse.json(db.instances)),
  http.get('/api/v1/instances/:id', ({ params }) => {
    const instance = find(String(params.id));
    return instance
      ? HttpResponse.json(instance)
      : problem(404, 'not_found', `No instance '${String(params.id)}'.`);
  }),
  http.get('/api/v1/instances/:id/integration', ({ params }) => {
    const instance = find(String(params.id));
    if (!instance) {
      return problem(404, 'not_found', `No instance '${String(params.id)}'.`);
    }

    if (instance.state !== 'running') {
      const offline: GameIntegrationView = {
        provider: 'valheim-console',
        label: { en: 'Game API', vi: 'Game API' },
        capabilities: ['overview', 'players', 'announce', 'save', 'kick', 'ban', 'admin'],
        online: false,
        info: null,
        metrics: null,
        players: [],
      };
      return HttpResponse.json(offline);
    }

    const view: GameIntegrationView = {
      provider: 'valheim-console',
      label: { en: 'Game API', vi: 'Game API' },
      capabilities: ['overview', 'players', 'announce', 'save', 'kick', 'ban', 'admin'],
      online: true,
      info: {
        version: '0.220.5',
        serverName: instance.displayName,
        description: 'A world of Vikings, meadows and the occasional troll.',
        worldGuid: null,
        worldName: 'Midgard',
      },
      metrics: {
        serverFps: 59.8,
        currentPlayerNum: 2,
        serverFrameTime: 16.7,
        maxPlayerNum: 10,
        uptime: 18_342,
        baseCampNum: 3,
        days: 412,
      },
      players: [
        {
          name: 'Ragnhild',
          accountName: null,
          playerId: null,
          userId: 'steam_76561198000000001',
          ip: '26.14.83.2',
          ping: null,
          x: null,
          y: null,
          level: null,
          buildingCount: null,
          online: true,
          isAdmin: true,
        },
        {
          name: 'Bjorn',
          accountName: null,
          playerId: null,
          userId: 'steam_76561198000000002',
          ip: '26.14.83.3',
          ping: null,
          x: null,
          y: null,
          level: null,
          buildingCount: null,
          online: true,
          isAdmin: false,
        },
      ],
    };
    return HttpResponse.json(view);
  }),
  http.post('/api/v1/instances', async ({ request }) => {
    const body = (await request.json()) as CreateInstanceRequest;
    if (find(body.id)) {
      return problem(409, 'conflict', `Instance '${body.id}' already exists.`);
    }

    const shift = db.instances.length * 10;
    const instance: InstanceSnapshot = {
      id: body.id,
      displayName: body.displayName ?? body.id,
      adapterId: body.adapterId,
      desiredState: 'stopped',
      state: 'notInstalled',
      activeJobId: null,
      runnerPid: null,
      gamePid: null,
      recentRestarts: 0,
      lastProblem: null,
      ports: { game: 2456 + shift, query: 2457 + shift },
    };
    db.instances.push(instance);
    db.settings[body.id] = {
      settings: body.settings ?? {},
      requiredSettings: ['serverName', 'world'],
      secrets: { password: { set: false } },
      restartRequired: false,
    };
    return HttpResponse.json(instance, { status: 201 });
  }),
  http.delete('/api/v1/instances/:id', ({ params }) => {
    const id = String(params.id);
    db.instances = db.instances.filter((instance) => instance.id !== id);
    return HttpResponse.json({ deleted: id, leftovers: null });
  }),

  http.post('/api/v1/instances/:id/start', ({ params }) => {
    const instance = find(String(params.id));
    if (!instance) {
      return problem(404, 'not_found', 'No such instance.');
    }

    if (instance.state === 'notInstalled') {
      return problem(
        409,
        'invalid_state',
        `The game is not installed. Run: gamewarden install ${instance.id}`
      );
    }

    setState(instance.id, 'starting', {
      desiredState: 'running',
      runnerPid: 8000 + db.instances.indexOf(instance),
      lastProblem: null,
    });
    log(instance.id, 'Started valheim_server.exe.', 'runner');
    after(2500, () => {
      setState(instance.id, 'running', { gamePid: 9100 });
      log(instance.id, 'Game server connected');
    });
    return HttpResponse.json(
      { message: 'Accepted.', instance: find(instance.id) },
      { status: 202 }
    );
  }),
  http.post('/api/v1/instances/:id/stop', ({ params }) => {
    const instance = find(String(params.id));
    if (!instance) {
      return problem(404, 'not_found', 'No such instance.');
    }

    setState(instance.id, 'stopping', { desiredState: 'stopped' });
    log(instance.id, 'Sent CTRL+C.', 'runner');
    after(2000, () => {
      log(instance.id, 'World saved ( 702.40ms )');
      setState(instance.id, 'stopped', { runnerPid: null, gamePid: null });
    });
    return HttpResponse.json(
      { message: 'Accepted.', instance: find(instance.id) },
      { status: 202 }
    );
  }),
  http.post('/api/v1/instances/:id/restart', ({ params }) => {
    const instance = find(String(params.id));
    if (!instance) {
      return problem(404, 'not_found', 'No such instance.');
    }

    setState(instance.id, 'stopping');
    after(1500, () => setState(instance.id, 'starting'));
    after(3500, () => setState(instance.id, 'running'));
    return HttpResponse.json(
      { message: 'Restarting.', instance: find(instance.id) },
      { status: 202 }
    );
  }),
  http.post('/api/v1/instances/:id/console', async ({ params, request }) => {
    const id = String(params.id);
    const body = (await request.json()) as { command: string };
    log(id, `> ${body.command}`, 'runner');
    return HttpResponse.json({ message: 'Accepted.', instance: find(id) }, { status: 202 });
  }),
  http.get('/api/v1/instances/:id/console', ({ params }) =>
    HttpResponse.json({ lines: db.console[String(params.id)] ?? [] })
  ),

  http.post('/api/v1/instances/:id/install', ({ params }) => {
    const instance = find(String(params.id));
    if (!instance) {
      return problem(404, 'not_found', 'No such instance.');
    }

    const job = startJob(instance, 'install');
    return HttpResponse.json({ jobId: job.id, job }, { status: 202 });
  }),
  http.post('/api/v1/instances/:id/update', ({ params }) => {
    const instance = find(String(params.id));
    if (!instance) {
      return problem(404, 'not_found', 'No such instance.');
    }

    const job = startJob(instance, 'update');
    return HttpResponse.json({ jobId: job.id, job }, { status: 202 });
  }),

  http.get('/api/v1/instances/:id/settings', ({ params }) => {
    const settings = db.settings[String(params.id)];
    return settings ? HttpResponse.json(settings) : problem(404, 'not_found', 'No such instance.');
  }),
  http.put('/api/v1/instances/:id/settings', async ({ params, request }) => {
    const id = String(params.id);
    const body = (await request.json()) as SettingsUpdate;
    const current = db.settings[id];
    if (!current) {
      return problem(404, 'not_found', 'No such instance.');
    }

    const secrets = { ...current.secrets };
    for (const name of Object.keys(body.secrets ?? {})) {
      secrets[name] = { set: true };
    }

    db.settings[id] = {
      ...current,
      settings: { ...current.settings, ...(body.settings ?? {}) },
      secrets,
      restartRequired: find(id)?.state === 'running',
    };
    return HttpResponse.json(db.settings[id]);
  }),

  http.get('/api/v1/metrics', () => {
    // A new sample per request at most every 5 s, like Core's sampler; only running servers have one.
    const last = db.metrics.machine.latest;
    if (!last || Date.now() - Date.parse(last.at) >= 5_000) {
      const sample = (id: string) =>
        metricPoint(new Date(), metricBases[id] ?? { cpu: 5, ramMb: 900 });
      db.metrics = addMetricsSample(
        db.metrics,
        sample('machine'),
        Object.fromEntries(
          db.instances
            .filter((server) => server.state === 'running')
            .map((server) => [server.id, sample(server.id)])
        )
      );
    }

    return HttpResponse.json(db.metrics);
  }),
  http.get('/api/v1/activity', ({ request }) => {
    const instanceId = new URL(request.url).searchParams.get('instanceId');
    return HttpResponse.json(
      instanceId ? db.activity.filter((item) => item.instanceId === instanceId) : db.activity
    );
  }),
  http.get('/api/v1/jobs', ({ request }) => {
    const instanceId = new URL(request.url).searchParams.get('instanceId');
    return HttpResponse.json(
      instanceId ? db.jobs.filter((job) => job.instanceId === instanceId) : db.jobs
    );
  }),
  http.get('/api/v1/jobs/:jobId', ({ params }) => {
    const job = db.jobs.find((candidate) => candidate.id === String(params.jobId));
    return job ? HttpResponse.json(job) : problem(404, 'not_found', 'No such job.');
  }),
  http.get('/api/v1/jobs/:jobId/log', ({ params }) => {
    const job = db.jobs.find((candidate) => candidate.id === String(params.jobId));
    if (!job) {
      return problem(404, 'not_found', 'No such job.');
    }

    if (job.type === 'mods.sync') {
      return HttpResponse.json({
        lines: [
          `mods.sync of ${job.instanceId}: loader bepinex.`,
          'Stage: resolve',
          'RandyKnapp-EpicLoot 0.15.4: chosen.',
          'Stage: download',
          'RandyKnapp-EpicLoot-0.15.4: downloaded 1843211 bytes.',
          ...(job.finishedAt ? ['Stage: build', 'Stage: activate', job.message ?? ''] : []),
        ],
      });
    }

    const lines = [
      `${job.type} of ${job.instanceId} (Valheim, Steam app 896660).`,
      'Stage: steamcmd.prepare',
      'SteamCMD self-update (try 1 of 3).',
      'Stage: steamcmd.install',
      ` Update state (0x61) downloading, progress: ${(job.progress ?? 0).toFixed(2)} (${Math.round((job.progress ?? 0) * 10)} / 1000)`,
      ...(job.state === 'succeeded'
        ? ["Success! App '896660' fully installed.", job.message ?? '']
        : []),
      ...(job.state === 'failed' ? [job.message ?? ''] : []),
    ];
    return HttpResponse.json({ lines });
  }),
];
