import { http, HttpResponse } from 'msw/http';
import type {
  InstanceSnapshot,
  JobInfo,
  ModCodePreview,
  ModEntryRequest,
  ModEntryView,
  ModSearchHit,
  ModSetView,
  ModsUpdate,
  ModsView,
} from '@/api/generated';

// Mods for `npm run dev:mock` and the Mods tab's tests (docs/13 §4.10): a Valheim server with the group's mods, a
// newer version, a share code and configs. Package names, versions and descriptions are Thunderstore's (checked
// 2026-10-30); download counts are rounded. Sets build in a few seconds as a `mods.sync` job.

const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

const loader = 'denikson-BepInExPack_Valheim';

/** What Thunderstore lists, as search shows it; `latest` is the newest version. */
const catalog: ModSearchHit[] = [
  hit(
    'RandyKnapp-EpicLoot',
    '0.15.4',
    'Adds loot drops, magic items, and enchanting to Valheim.',
    2_395_700
  ),
  hit(
    'ValheimModding-Jotunn',
    '2.30.2',
    "Jötunn (/ˈjɔːtʊn/, 'giant'), the Valheim Library was created with the goal of making the lives of mod developers easier.",
    4_909_899
  ),
  hit(
    'ValheimModding-JsonDotNET',
    '13.0.4',
    'Shared version 13.0.3 of Json.NET from Newtonsoft, net45 package for use in Valheim mods.',
    914_175
  ),
  hit(
    loader,
    '5.4.2360',
    'BepInEx pack for Valheim. Preconfigured with the correct entry point for mods and preferred defaults.',
    7_543_408
  ),
  hit(
    'SephrinMods-VikingNPC_Continued',
    '0.4.2',
    'Community-maintained continuation of VikingNPC (Norsemen), updated for compatibility with the latest Valheim.',
    1_459
  ),
  hit(
    'RustyMods-VikingNPC',
    '0.3.4',
    'Tameable vikings that can mine, lumber or fish. Friendly unless provoked',
    79_935,
    true
  ),
  hit(
    'Advize-PlantEverything',
    '1.21.4',
    'Allows your cultivator to plant berry bushes, thistle, dandelions, mushrooms, previously unplantable items.',
    2_930_052
  ),
  hit(
    'RandyKnapp-EquipmentAndQuickSlots',
    '3.2.0',
    'Give equipped items their own dedicated inventory slots, plus up to six hotkeyable quick slots.',
    1_593_888
  ),
  hit(
    'shudnal-ExtraSlots',
    '1.2.21',
    'More inventory slots dedicated for equipment, food, ammo and misc items.',
    1_389_477
  ),
  hit(
    'shudnal-Seasons',
    '1.10.5',
    'Four customizable seasons. Different visual flavor for every season, seasonal impact on gameplay.',
    350_461
  ),
  hit(
    'MSchmoecker-MultiUserChest',
    '0.6.2',
    'Allows multiple players to interact with the same chest at the same time',
    805_953
  ),
  hit(
    'Azumatt-AzuCraftyBoxes',
    '1.8.19',
    'AzuCraftyBoxes is a Valheim mod that allows players to access and use resources from nearby containers.',
    1_644_389,
    true
  ),
];

/** Exact dependencies, as Thunderstore names them per version (abridged). */
const dependencies: Record<string, string[]> = {
  'RandyKnapp-EpicLoot': ['ValheimModding-JsonDotNET', 'ValheimModding-Jotunn', loader],
  'SephrinMods-VikingNPC_Continued': ['ValheimModding-Jotunn', loader],
  'ValheimModding-Jotunn': [loader],
};

const groupCode = '0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13';

function hit(
  pkg: string,
  version: string,
  description: string,
  downloads: number,
  deprecated = false
): ModSearchHit {
  const [owner = '', name = ''] = pkg.split(/-(.*)/);
  return {
    package: pkg,
    name,
    owner,
    version,
    description,
    categories: ['Mods'],
    deprecated,
    downloads,
    rating: Math.round(downloads / 10_000),
    url: `https://thunderstore.io/c/valheim/p/${owner}/${name}/`,
  };
}

const entry = (
  pkg: string,
  version: string,
  origin = 'dependency',
  enabled = true
): ModEntryView => ({
  package: pkg,
  version,
  enabled,
  origin,
});

const set = (id: number, minutes: number, origin: string, entries: ModEntryView[]): ModSetView => ({
  id,
  createdAt: ago(minutes),
  origin,
  state: 'ready',
  jobId: `20261028-0${id}0000-${id}a${id}b${id}c`,
  entries,
});

function emptyMods(instanceId: string): ModsView {
  return {
    instanceId,
    loader: 'bepinex',
    enabled: false,
    active: null,
    previous: null,
    pending: null,
    shareCode: null,
    shareCodeCreatedAt: null,
    newerVersions: null,
    community: 'valheim',
    runningSetId: null,
    restartRequired: false,
  };
}

/** The running server got a new set from the group's code after its last start: Restart to apply shows. */
export function initialMods(): Record<string, ModsView> {
  return {
    'valheim-main': {
      ...emptyMods('valheim-main'),
      enabled: true,
      active: set(7, 50, 'code', [
        entry('RandyKnapp-EpicLoot', '0.15.3', 'chosen'),
        entry('SephrinMods-VikingNPC_Continued', '0.4.2', 'chosen'),
        entry('Advize-PlantEverything', '1.21.4', 'chosen', false),
        entry(loader, '5.4.2351'),
        entry('ValheimModding-Jotunn', '2.30.2'),
        entry('ValheimModding-JsonDotNET', '13.0.4'),
      ]),
      previous: set(6, 3 * 24 * 60, 'edit', [
        entry('RandyKnapp-EpicLoot', '0.15.3', 'chosen'),
        entry(loader, '5.4.2351'),
        entry('ValheimModding-Jotunn', '2.30.2'),
        entry('ValheimModding-JsonDotNET', '13.0.4'),
      ]),
      shareCode: '019a2b7e-55c1-7d0e-a8f3-1c9e4b2d6a70',
      shareCodeCreatedAt: ago(26 * 60),
      newerVersions: {
        checkedAt: ago(45),
        updates: [
          { package: 'RandyKnapp-EpicLoot', current: '0.15.3', latest: '0.15.4' },
          { package: loader, current: '5.4.2351', latest: '5.4.2360' },
        ],
        states: [
          'RandyKnapp-EpicLoot',
          'SephrinMods-VikingNPC_Continued',
          loader,
          'ValheimModding-Jotunn',
          'ValheimModding-JsonDotNET',
        ].map((pkg) => ({ package: pkg, state: 'ok' as const })),
      },
      runningSetId: 6,
    },
  };
}

const configs = [
  {
    path: 'BepInEx.cfg',
    size: 3_912,
    text: '[Logging.Console]\n\n## Enables showing a console for log output.\nEnabled = false\n',
  },
  {
    path: 'randyknapp.mods.epicloot.cfg',
    size: 8_420,
    text: '## Settings file was created by plugin Epic Loot v0.15.3\n\n[Balance]\n\n## The chance a magic item drops.\nItemsToMaterialsDropRatio = 0\n\n[Crafting UI]\n\nUseScrollingCraftDescription = true\n',
  },
  {
    path: 'EpicLoot/patches/RelicHeimPatches/Adventure_RelicHeim.json',
    size: 15_204,
    text: '{\n  "TargetFile": "adventuredata.json",\n  "Patches": []\n}\n',
  },
  {
    path: 'com.jotunn.jotunn.cfg',
    size: 612,
    text: '[Server]\n\n## Show the version mismatch message in the main menu.\nShowVersionMismatch = true\n',
  },
  {
    path: 'sephrin.vikingnpc.cfg',
    size: 2_048,
    text: '[1 - General]\n\n## If on, the configuration is locked and can be changed by server admins only.\nLock Configuration = On\n',
  },
];

const latest = new Map(catalog.map((item) => [item.package, item.version]));

/** Chosen mods plus their dependencies at the latest version, as Core's resolver adds them (abridged). */
function resolve(chosen: ModEntryRequest[]): ModEntryView[] {
  const entries = chosen.map((choice) =>
    entry(
      choice.package,
      choice.version ?? latest.get(choice.package) ?? '1.0.0',
      'chosen',
      choice.enabled ?? true
    )
  );
  for (const choice of chosen.filter((item) => item.enabled !== false)) {
    for (const dependency of dependencies[choice.package] ?? []) {
      if (!entries.some((item) => item.package === dependency)) {
        entries.push(entry(dependency, latest.get(dependency) ?? '1.0.0'));
      }
    }
  }
  return entries;
}

const problem = (status: number, code: string, title: string) =>
  HttpResponse.json(
    { status, title, code },
    { status, headers: { 'Content-Type': 'application/problem+json' } }
  );

/** The mods endpoints over the mock Core's instances and jobs. */
export function modsHandlers(core: {
  find: (id: string) => InstanceSnapshot | undefined;
  addJob: (job: JobInfo) => void;
  updateJob: (id: string, changes: Partial<JobInfo>) => void;
}) {
  const state = initialMods();
  let nextSet = 8;
  const view = (id: string): ModsView => {
    state[id] ??= emptyMods(id);
    const mods = state[id];
    const running = core.find(id)?.state === 'running';
    const restartRequired =
      running &&
      (mods.enabled
        ? !!mods.active && mods.active.id !== mods.runningSetId
        : mods.runningSetId !== null);
    return { ...mods, restartRequired };
  };
  const known = (id: string) =>
    core.find(id) ? null : problem(404, 'not_found', `No instance '${id}'.`);

  function build(id: string, origin: string, chosen: ModEntryRequest[]) {
    const mods = view(id);
    const job: JobInfo = {
      id: `${new Date().toISOString().replace(/\D/g, '').slice(0, 8)}-${Math.random().toString(16).slice(2, 8)}`,
      type: 'mods.sync',
      instanceId: id,
      state: 'running',
      stage: 'resolve',
      progress: null,
      message: null,
      createdAt: new Date().toISOString(),
      finishedAt: null,
    };
    const pending: ModSetView = {
      ...set(nextSet++, 0, origin, resolve(chosen)),
      state: 'building',
      jobId: job.id,
    };
    state[id] = { ...mods, pending };
    core.addJob(job);
    setTimeout(() => core.updateJob(job.id, { stage: 'download', progress: 40 }), 1200);
    setTimeout(() => core.updateJob(job.id, { stage: 'build', progress: null }), 2600);
    setTimeout(() => {
      const current = view(id);
      state[id] = {
        ...current,
        previous: current.active,
        active: { ...pending, state: 'ready' },
        pending: null,
      };
      core.updateJob(job.id, {
        state: 'succeeded',
        stage: 'activate',
        progress: 100,
        message: `Mod set ${pending.id} is active; it takes effect at the next start.`,
        finishedAt: new Date().toISOString(),
      });
    }, 3800);
    return HttpResponse.json({ jobId: job.id, job }, { status: 202 });
  }

  return [
    http.get(
      '/api/v1/instances/:id/mods',
      ({ params }) => known(String(params.id)) ?? HttpResponse.json(view(String(params.id)))
    ),
    http.put('/api/v1/instances/:id/mods', async ({ params, request }) => {
      const id = String(params.id);
      const body = (await request.json()) as ModsUpdate;
      if (view(id).pending?.state === 'building') {
        return problem(409, 'mods_busy', `A mods job runs for '${id}'; wait for it to finish.`);
      }

      if (body.previewId) {
        return build(id, 'code', previewOf(groupCode).entries);
      }

      return build(id, 'edit', body.entries ?? []);
    }),
    http.put('/api/v1/instances/:id/mods/enabled', async ({ params, request }) => {
      const id = String(params.id);
      const { enabled } = (await request.json()) as { enabled: boolean };
      state[id] = { ...view(id), enabled };
      return HttpResponse.json(view(id));
    }),
    http.post('/api/v1/instances/:id/mods/rollback', ({ params }) => {
      const id = String(params.id);
      const mods = view(id);
      if (!mods.previous) {
        return problem(409, 'invalid_state', `'${id}' has no previous mod set to go back to.`);
      }

      state[id] = { ...mods, active: mods.previous, previous: mods.active };
      return HttpResponse.json(view(id));
    }),
    http.post('/api/v1/instances/:id/mods/code-preview', async ({ request }) => {
      const { code } = (await request.json()) as { code: string };
      const key = code.trim().toLowerCase();
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(key)) {
        return problem(400, 'validation_failed', `'${code}' is not an r2modman profile code.`);
      }

      return key === groupCode
        ? HttpResponse.json(previewOf(key))
        : problem(
            404,
            'not_found',
            `The code ${key} is unknown or expired; ask for a new code (r2modman: Export profile as code).`
          );
    }),
    http.post('/api/v1/instances/:id/mods/share-code', ({ params }) => {
      const id = String(params.id);
      const code = crypto.randomUUID();
      const createdAt = new Date().toISOString();
      state[id] = { ...view(id), shareCode: code, shareCodeCreatedAt: createdAt };
      return HttpResponse.json({ code, createdAt });
    }),
    http.post('/api/v1/instances/:id/mods/check-updates', ({ params }) => {
      const id = String(params.id);
      const mods = view(id);
      if (!mods.active) {
        return problem(409, 'invalid_state', `'${id}' has no active mod set to check.`);
      }

      const installed = mods.active.entries.filter((item) => item.enabled);
      const check = {
        checkedAt: new Date().toISOString(),
        updates: installed
          .filter((item) => latest.has(item.package) && latest.get(item.package) !== item.version)
          .map((item) => ({
            package: item.package,
            current: item.version,
            latest: latest.get(item.package)!,
          })),
        states: installed.map((item) => ({
          package: item.package,
          state: !latest.has(item.package)
            ? ('notFound' as const)
            : catalog.find((candidate) => candidate.package === item.package)?.deprecated
              ? ('deprecated' as const)
              : ('ok' as const),
        })),
      };
      state[id] = { ...mods, newerVersions: check };
      return HttpResponse.json(check);
    }),
    http.get('/api/v1/instances/:id/mods/search', ({ request }) => {
      const url = new URL(request.url);
      const q = (url.searchParams.get('q') ?? '').trim().toLowerCase();
      if (!q) {
        return problem(400, 'validation_failed', 'q: give 1 to 100 characters to search for.');
      }

      const compact = q.replace(/[\s_-]/g, '');
      const rank = (item: ModSearchHit) => {
        const name = item.name.toLowerCase().replace(/_/g, '');
        return name === compact
          ? 0
          : name.startsWith(compact)
            ? 1
            : name.includes(compact)
              ? 2
              : item.owner.toLowerCase().includes(q)
                ? 3
                : item.description.toLowerCase().includes(q)
                  ? 4
                  : -1;
      };
      const results = catalog
        .map((item) => ({ item, rank: rank(item) }))
        .filter((match) => match.rank >= 0)
        .sort(
          (a, b) =>
            a.rank - b.rank ||
            Number(a.item.deprecated) - Number(b.item.deprecated) ||
            b.item.downloads - a.item.downloads
        )
        .slice(0, Number(url.searchParams.get('limit') ?? 20))
        .map((match) => match.item);
      return HttpResponse.json({ community: 'valheim', fetchedAt: ago(90), results });
    }),
    http.get('/api/v1/instances/:id/mods/configs', ({ params }) => {
      const mods = view(String(params.id));
      return HttpResponse.json({
        setId: mods.active?.id ?? null,
        files: mods.active
          ? configs.map(({ path, size }, index) => ({ path, size, modified: ago(40 + index * 7) }))
          : [],
      });
    }),
    http.get('/api/v1/instances/:id/mods/configs/content', ({ params, request }) => {
      const mods = view(String(params.id));
      const path = new URL(request.url).searchParams.get('path');
      const file = configs.find((candidate) => candidate.path === path);
      return file && mods.active
        ? HttpResponse.json({
            setId: mods.active.id,
            path: file.path,
            size: file.size,
            modified: ago(40),
            text: file.text,
          })
        : problem(404, 'not_found', `No config file '${path ?? ''}'.`);
    }),
    http.get('/api/v1/instances/:id/mods/log', ({ params }) => {
      const mods = view(String(params.id));
      const setId = mods.runningSetId ?? mods.active?.id ?? null;
      return HttpResponse.json({ setId, lines: setId === null ? [] : loaderLog });
    }),
  ];
}

function previewOf(code: string): ModCodePreview {
  return {
    previewId: '5f0c2a9e6b1d4e8f',
    code,
    profileName: 'Vikings 2026',
    expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
    entries: [
      { package: 'RandyKnapp-EpicLoot', version: '0.15.4', enabled: true, state: 'ok' },
      { package: 'SephrinMods-VikingNPC_Continued', version: '0.4.2', enabled: true, state: 'ok' },
      { package: 'ValheimModding-Jotunn', version: '2.30.2', enabled: true, state: 'ok' },
      { package: 'ValheimModding-JsonDotNET', version: '13.0.4', enabled: true, state: 'ok' },
      { package: loader, version: '5.4.2360', enabled: true, state: 'ok' },
      { package: 'RustyMods-VikingNPC', version: '0.3.4', enabled: false, state: 'deprecated' },
    ],
    warnings: [
      "Left out 1 file(s) outside config/; only the profile's BepInEx config folder is used: BepInEx/plugins/Minimap/settings.json.",
    ],
    configFiles: [
      'randyknapp.mods.epicloot.cfg',
      'EpicLoot/patches/RelicHeimPatches/Adventure_RelicHeim.json',
      'sephrin.vikingnpc.cfg',
    ],
  };
}

const loaderLog = [
  '[Message:   BepInEx] BepInEx 5.4.23.6 - valheim_server (10/30/2026 2:14:07 PM)',
  '[Info   :   BepInEx] Running under Unity 6000.0.75f1',
  '[Info   :   BepInEx] Chainloader ready',
  '[Info   :   BepInEx] Loading [Jotunn 2.30.2]',
  '[Info   :   BepInEx] Loading [Epic Loot 0.15.3]',
  '[Info   :Epic Loot] Loaded 412 magic item effects',
  '[Info   :   BepInEx] Chainloader startup complete',
  '[Info   :    Jotunn] Server: version check passed for 3 mods',
];
