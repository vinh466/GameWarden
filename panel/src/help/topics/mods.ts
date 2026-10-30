import type { HelpTopic } from '@/components/help/types';
import { repoDoc } from './sources';

// The Mods tab (docs/13 §4.10). Sources checked 2026-10-30.
const checked = '2026-10-30';

const r2modman = {
  title: 'r2modmanPlus: ProfilesClient.ts (profile codes)',
  url: 'https://github.com/ebkr/r2modmanPlus/blob/develop/src/r2mm/profiles/ProfilesClient.ts',
  publisher: 'r2modmanPlus',
  checked,
};

const jotunn = {
  title: 'Jötunn: NetworkCompatibility',
  url: 'https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html',
  publisher: 'Valheim Modding',
  checked,
};

export const modsTopics = {
  'mods.on': {
    title: 'Mods on',
    summary:
      'On, the server loads the active mod set at its next start; off, it runs the game without mods. Either way the game folder stays as SteamCMD installed it.',
    body: [
      {
        p: 'Each mod set is built in a folder of its own, outside the game folder. A start with Mods on passes BepInEx the set to load; a start with Mods off tells it to load nothing. The only files GameWarden puts next to the game are BepInEx’s two small entry files, which load nothing by themselves.',
      },
      {
        list: [
          'A change applies at the next start: while the server runs, the tab shows Restart to apply.',
          'With Mods on and no ready set, a start is refused, so the server never runs half-modded.',
          'Turning mods off is the quick way to run the server vanilla, for example after a game update that broke a mod.',
        ],
      },
      {
        tip: 'Friends need the same mods at the same versions to join: give them the share code from this tab.',
      },
    ],
    tested:
      'Spike S-M1 on a Windows runner, 2026-10-30: with Mods on, BepInEx 5.4.2360 loaded Jötunn and Epic Loot from a set folder outside the game; with Mods off nothing loaded and the server ran (docs/13 §5).',
    verified: true,
    sources: [
      repoDoc('docs/13-mods.md#43-folders-and-launch', '13 Mods, §4.3 Folders and launch', checked),
      repoDoc(
        'decisions/0020-mods-in-set-folders-as-pinned-sets.md',
        'ADR-0020: Mods in set folders as pinned sets',
        checked
      ),
      {
        title: 'r2modmanPlus: BepInExGameInstructions.ts (Doorstop arguments)',
        url: 'https://github.com/ebkr/r2modmanPlus/blob/develop/src/r2mm/launching/instructions/instructions/loader/BepInExGameInstructions.ts',
        publisher: 'r2modmanPlus',
        checked,
      },
    ],
    related: ['mods.versions', 'mods.configs'],
  },

  'mods.codes': {
    title: 'r2modman codes',
    summary:
      'A profile code is how r2modman shares a list of mods: a short key that Thunderstore stores the list under. GameWarden reads codes and makes them, so the server and every friend run the same mods.',
    body: [
      {
        p: 'Import: paste a code made with r2modman’s Export profile as code. GameWarden shows what it holds before anything changes, each mod with its version, whether it is enabled and what Thunderstore says about it. Apply builds a new set with exactly those versions; mods disabled in the code stay in the set, disabled. The code’s config files go over the server’s current ones when the set is built; other files in it are listed as warnings and left out.',
      },
      {
        p: 'Share: GameWarden makes a code of the active set’s enabled mods. A friend opens r2modman, chooses Import profile, then From code, and pastes it. The code carries no config files.',
      },
      {
        warning:
          'Thunderstore keeps codes for a limited time, and how long is not documented: an old code can answer “not found”. Make a new one when that happens. Thunderstore also makes at most a few new codes a minute.',
      },
    ],
    verified: false,
    sources: [
      repoDoc('docs/13-mods.md#44-getting-a-mod-set', '13 Mods, §4.4 Getting a mod set', checked),
      repoDoc(
        'docs/13-mods.md#46-sharing-with-friends',
        '13 Mods, §4.6 Sharing with friends',
        checked
      ),
      r2modman,
    ],
    related: ['mods.versions', 'mods.configs'],
  },

  'mods.versions': {
    title: 'Why versions must match',
    summary:
      'Many Valheim mods refuse a player whose mods differ from the server’s: the connection fails with a version mismatch until both sides have the same versions.',
    body: [
      {
        p: 'Jötunn, the library many Valheim mods use, lets each mod declare what must match. With EveryoneMustHaveMod, the mod must be on the server and on every client, at a version that matches as strictly as the mod asks. When a player connects, the server compares the lists and disconnects a player who does not match, naming the mod and the versions.',
      },
      {
        p: 'That is why GameWarden pins every mod to an exact version and never updates by itself: an update on the server alone would lock friends out. After an update, share a new code so friends update too.',
      },
      {
        tip: 'Back to the previous set undoes an update in one step if friends cannot join.',
      },
    ],
    verified: true,
    sources: [
      jotunn,
      repoDoc('docs/13-mods.md#3-facts-checked-2026-10-30', '13 Mods, §3 Facts', checked),
      repoDoc('docs/13-mods.md#48-updates', '13 Mods, §4.8 Updates', checked),
    ],
    related: ['mods.codes'],
  },

  'mods.configs': {
    title: 'Config files',
    summary:
      'Mods keep their settings in BepInEx config files. The active mod set’s files are the server’s config: open one to view it, edit it, or go back to an earlier version. For mods that sync their settings, the server’s files win over the players’.',
    body: [
      {
        p: 'Edit opens a text editor (JSON files are checked as you type and again before saving). Save writes the file the server uses at once. While the server runs, mods that watch their config file apply the change right away; the others apply it at the next restart, and the panel offers Restart now.',
      },
      {
        p: 'If the game, a mod or someone else wrote the file after you opened it, nothing is saved: your text stays, and you can load the server’s version or compare it with yours.',
      },
      {
        p: 'History keeps every save: before a save, what was on the server (On the server), then your text (Edited). Select a revision to compare it line by line with the current file, and Restore it; a restore is a new revision, so it can be undone too. The newest 50 revisions of each file are kept, also when the mods change.',
      },
      {
        p: 'A new mod set gets its config files when it is built: first the mods’ defaults, then the current set’s files (so your changes carry over), then those of the r2modman code it came from. Back to the previous set keeps the current files too. An update never overwrites a file that exists.',
      },
      {
        p: 'Mods such as Epic Loot use ServerSync: when a player joins, the server sends its synced settings and, when the mod locks them, the player’s own values are not used. Other settings, such as a minimap’s or a UI mod’s, stay personal; that is why the share code carries no config files.',
      },
    ],
    verified: false,
    sources: [
      repoDoc('docs/13-mods.md#47-configs', '13 Mods, §4.7 Configs', checked),
      repoDoc(
        'decisions/0021-mod-configs-edited-with-revisions.md',
        'ADR-0021: mod config files edited in the panel, with revisions',
        '2026-10-31'
      ),
      {
        title: 'ServerSync: README',
        url: 'https://github.com/blaxxun-boop/ServerSync',
        publisher: 'blaxxun-boop',
        checked,
      },
    ],
    related: ['mods.codes', 'mods.on'],
  },
} satisfies Record<string, HelpTopic>;
