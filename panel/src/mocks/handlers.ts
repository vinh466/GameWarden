import { http, HttpResponse } from 'msw/http';
import type {
  ActualState,
  CreateInstanceRequest,
  InstanceSnapshot,
  JobInfo,
  SettingsUpdate,
  SystemSettingsUpdate,
  SystemSettingsView,
  UpdateRequest,
} from '@/api/generated';
import { addMetricsSample } from '@/api/realtimeCache';
import {
  adapters,
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

// A small in-memory Core for `npm run dev:mock` (docs/11 §14 phase 1): commands move states the way Core does, jobs make
// progress, a running server prints now and then. Routes and bodies follow the generated types.

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
    if (job.state !== 'running') {
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

  http.get('/api/v1/system', () => HttpResponse.json(system)),
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
  http.get('/api/v1/system/versions', () =>
    HttpResponse.json({
      current: '0.2.0-dev',
      installed: ['0.2.0-dev', '0.1.0-rc.2'],
      previous: '0.1.0-rc.2',
      canRestoreDatabase: true,
    })
  ),
  http.post('/api/v1/system/versions/switch', () =>
    problem(409, 'invalid_state', 'The mock API cannot switch versions.')
  ),
  http.get('/api/v1/system/update', ({ request }) => {
    const prerelease = new URL(request.url).searchParams.get('prerelease') === 'true';
    return HttpResponse.json({
      current: system.version,
      latest: prerelease ? '0.3.0-rc.1' : '0.2.0',
      available: prerelease,
      url: null,
    });
  }),
  http.post('/api/v1/system/update', async ({ request }) => {
    const body = (await request.json()) as UpdateRequest;
    const to = body.prerelease ? '0.3.0-rc.1' : '0.2.0';
    return HttpResponse.json(
      { from: system.version, to, message: `Updating to ${to}.` },
      { status: 202 }
    );
  }),

  http.get('/api/v1/adapters', () => HttpResponse.json({ adapters, problems: [] })),
  http.post('/api/v1/adapters/reload', () =>
    HttpResponse.json({ adapters: adapters.length, problems: [] })
  ),

  http.get('/api/v1/instances', () => HttpResponse.json(db.instances)),
  http.get('/api/v1/instances/:id', ({ params }) => {
    const instance = find(String(params.id));
    return instance
      ? HttpResponse.json(instance)
      : problem(404, 'not_found', `No instance '${String(params.id)}'.`);
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
