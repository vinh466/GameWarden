import { http, HttpResponse } from 'msw/http';
import type {
  ModConfigRestore,
  ModConfigRevisionContent,
  ModConfigRevisionKind,
  ModConfigUpdate,
  ModsView,
} from '@/api/generated';

// The active set's config files (docs/13 §4.7) with revisions (ADR-0021), for `npm run dev:mock` and the panel tests.
// Epic Loot's file has three revisions, the oldest what the game wrote; the others have none until they are saved.

const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

const problem = (status: number, code: string, title: string) =>
  HttpResponse.json(
    { status, title, code },
    { status, headers: { 'Content-Type': 'application/problem+json' } }
  );

/** A stand-in for SHA-256 in the mock (FNV-1a, 64 hex digits): the panel only compares it. */
export function mockSha(text: string) {
  let hex = '';
  for (let seed = 0; seed < 8; seed++) {
    let hash = 0x811c9dc5 ^ seed;
    for (let index = 0; index < text.length; index++) {
      hash = Math.imul(hash ^ text.charCodeAt(index), 0x01000193);
    }

    hex += (hash >>> 0).toString(16).padStart(8, '0');
  }

  return hex;
}

const bytes = (text: string) => new TextEncoder().encode(text).length;

const epicLootServer =
  '## Settings file was created by plugin Epic Loot v0.15.3\n\n[Balance]\n\n## The chance a magic item drops.\nItemsToMaterialsDropRatio = 0\n\n[Crafting UI]\n\nUseScrollingCraftDescription = true\n';
const epicLootEdited = epicLootServer.replace(
  'ItemsToMaterialsDropRatio = 0',
  'ItemsToMaterialsDropRatio = 0.3'
);
const epicLoot = epicLootEdited.replace(
  'UseScrollingCraftDescription = true',
  'UseScrollingCraftDescription = true\n\n[Abilities]\n\n## Seconds between two uses of an ability.\nAbilityCooldown = 45'
);

const files = () =>
  new Map(
    [
      {
        path: 'BepInEx.cfg',
        text: '[Logging.Console]\n\n## Enables showing a console for log output.\nEnabled = false\n',
      },
      { path: 'randyknapp.mods.epicloot.cfg', text: epicLoot },
      {
        path: 'EpicLoot/patches/RelicHeimPatches/Adventure_RelicHeim.json',
        text: '{\n  "TargetFile": "adventuredata.json",\n  "Patches": []\n}\n',
      },
      {
        path: 'com.jotunn.jotunn.cfg',
        text: '[Server]\n\n## Show the version mismatch message in the main menu.\nShowVersionMismatch = true\n',
      },
      {
        path: 'sephrin.vikingnpc.cfg',
        text: '[1 - General]\n\n## If on, the configuration is locked and can be changed by server admins only.\nLock Configuration = On\n',
      },
      {
        path: 'EpicLoot/translations/English.dat',
        text: 'binary-ish data the panel does not edit\n',
      },
    ].map((file, index) => [file.path, { ...file, modified: ago(40 + index * 7) }])
  );

const editable = (path: string) => /\.(cfg|json|ya?ml|txt|ini)$/i.test(path);

/** The config endpoints over one instance's mods state. */
export function modConfigHandlers(
  view: (id: string) => ModsView,
  known: (id: string) => Response | null,
  running: (id: string) => boolean
) {
  const store = files();
  let nextRevision = 104;
  const revisions: ModConfigRevisionContent[] = [
    [103, 'edit', epicLoot, 95],
    [102, 'edit', epicLootEdited, 60 * 26],
    [101, 'server', epicLootServer, 60 * 50],
  ].map(([id, kind, text, minutes]) => ({
    id: id as number,
    path: 'randyknapp.mods.epicloot.cfg',
    kind: kind as ModConfigRevisionKind,
    sha256: mockSha(text as string),
    size: bytes(text as string),
    createdAt: ago(minutes as number),
    text: text as string,
  }));
  const content = (id: string, path: string | null) => {
    const mods = view(id);
    const file = path === null ? undefined : store.get(path);
    return mods.active && file ? { setId: mods.active.id, file } : null;
  };
  const add = (path: string, kind: ModConfigRevisionKind, text: string) => {
    const revision = {
      id: nextRevision++,
      path,
      kind,
      sha256: mockSha(text),
      size: bytes(text),
      createdAt: new Date().toISOString(),
      text,
    };
    revisions.unshift(revision);
    return revision;
  };
  // An edit or a restore, with Core's rules: the base must be the file's hash; what was on disk is kept first.
  const write = (
    id: string,
    path: string,
    text: string,
    base: string | null,
    kind: ModConfigRevisionKind
  ) => {
    const found = content(id, path);
    if (!found) {
      return problem(404, 'not_found', `No config file '${path}'.`);
    }

    if (!editable(path)) {
      return problem(400, 'validation_failed', `'${path}' cannot be edited.`);
    }

    if (base !== mockSha(found.file.text)) {
      return problem(
        409,
        'config_changed',
        `'${path}' changed on the server since it was loaded; nothing was written.`
      );
    }

    if (path.endsWith('.json')) {
      try {
        JSON.parse(text);
      } catch (error) {
        return problem(400, 'validation_failed', `'${path}' is not valid JSON: ${String(error)}`);
      }
    }

    const newest = revisions.find((revision) => revision.path === path);
    if (newest?.sha256 !== mockSha(found.file.text)) {
      add(path, 'server', found.file.text);
    }

    const revision = add(path, kind, text);
    store.set(path, { ...found.file, text, modified: revision.createdAt });
    return HttpResponse.json({
      path,
      sha256: revision.sha256,
      size: revision.size,
      revisionId: revision.id,
      running: running(id),
    });
  };

  return [
    http.get('/api/v1/instances/:id/mods/configs', ({ params }) => {
      const mods = view(String(params.id));
      return HttpResponse.json({
        setId: mods.active?.id ?? null,
        files: mods.active
          ? [...store.values()].map(({ path, text, modified }) => ({
              path,
              size: bytes(text),
              modified,
            }))
          : [],
      });
    }),
    http.get('/api/v1/instances/:id/mods/configs/content', ({ params, request }) => {
      const path = new URL(request.url).searchParams.get('path');
      const found = content(String(params.id), path);
      return found
        ? HttpResponse.json({
            setId: found.setId,
            path: found.file.path,
            size: bytes(found.file.text),
            modified: found.file.modified,
            sha256: mockSha(found.file.text),
            editable: editable(found.file.path),
            text: found.file.text,
          })
        : problem(404, 'not_found', `No config file '${path ?? ''}'.`);
    }),
    http.put('/api/v1/instances/:id/mods/configs/content', async ({ params, request }) => {
      const id = String(params.id);
      const body = (await request.json()) as ModConfigUpdate;
      return (
        known(id) ??
        write(
          id,
          new URL(request.url).searchParams.get('path') ?? '',
          body.content ?? '',
          body.baseSha256,
          'edit'
        )
      );
    }),
    http.get('/api/v1/instances/:id/mods/configs/revisions', ({ request }) => {
      const path = new URL(request.url).searchParams.get('path') ?? '';
      return HttpResponse.json({
        path,
        revisions: revisions
          .filter((revision) => revision.path === path)
          .map(({ text: _text, path: _path, ...info }) => info),
      });
    }),
    http.get('/api/v1/instances/:id/mods/configs/revisions/:revisionId', ({ params }) => {
      const revision = revisions.find((candidate) => candidate.id === Number(params.revisionId));
      return revision
        ? HttpResponse.json(revision)
        : problem(404, 'not_found', `No config revision ${String(params.revisionId)}.`);
    }),
    http.post(
      '/api/v1/instances/:id/mods/configs/revisions/:revisionId/restore',
      async ({ params, request }) => {
        const id = String(params.id);
        const body = (await request.json()) as ModConfigRestore;
        const revision = revisions.find((candidate) => candidate.id === Number(params.revisionId));
        return (
          known(id) ??
          (revision
            ? write(id, revision.path, revision.text, body.baseSha256, 'restore')
            : problem(404, 'not_found', `No config revision ${String(params.revisionId)}.`))
        );
      }
    ),
  ];
}
