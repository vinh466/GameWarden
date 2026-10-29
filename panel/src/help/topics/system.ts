import type { HelpTopic } from '@/components/help/types';
import { repoDoc } from './sources';

// System page: network (also setup step 3) and updates. Sources checked 2026-10-29.
const checked = '2026-10-29';

export const systemTopics = {
  'system.vpnRanges': {
    title: 'VPN ranges (game ports open to)',
    summary:
      'The remote addresses the Windows firewall lets reach your game servers. Empty means the defaults: Radmin VPN (26.0.0.0/8) and Tailscale (100.64.0.0/10).',
    body: [
      {
        p: 'GameWarden adds one inbound firewall rule per game port, such as GameWarden:valheim-main:2456/udp, and allows only these ranges. A friend whose VPN address is outside every range cannot join. Saving changes every rule at once; running servers keep running.',
      },
      {
        list: [
          "Radmin VPN gives each computer a 26.x.x.x address. Radmin's own help names 26.0.0.0/8 in its firewall setup but does not state the range outright, so GameWarden's docs still mark it unverified (spike S7).",
          'Tailscale gives each device an address in 100.64.0.0/10, the shared address space of RFC 6598.',
          'Another VPN or your LAN: add its range in CIDR form, such as 192.168.1.0/24. Your list replaces the defaults, so add them back if friends still use Radmin or Tailscale.',
        ],
      },
      {
        p: '26.0.0.0/8 is not a private range: IANA lists it as a legacy block of the US Defense Information Systems Agency. Radmin uses it inside its own network.',
      },
      {
        warning:
          'Never add 0.0.0.0/0 or a public range: the game ports would be open to any address that can reach this machine.',
      },
    ],
    tested:
      '0.1.0-rc.1 on the template VM, 2026-10-28: with the rules limited to the default ranges, a friend joined Valheim over Radmin VPN (docs/10 §8.2).',
    verified: false,
    sources: [
      {
        title: 'Radmin VPN Help: Firewall setup',
        url: 'https://www.radmin-vpn.com/help/',
        publisher: 'Famatech',
        checked,
      },
      {
        title: 'What are these 100.x.y.z addresses?',
        url: 'https://tailscale.com/kb/1015/100.x-addresses',
        publisher: 'Tailscale',
        checked,
      },
      {
        title: 'RFC 6598: IANA-Reserved IPv4 Prefix for Shared Address Space',
        url: 'https://www.rfc-editor.org/rfc/rfc6598',
        publisher: 'IETF',
        checked,
      },
      {
        title: 'IPv4 Address Space registry',
        url: 'https://www.iana.org/assignments/ipv4-address-space/ipv4-address-space.xhtml',
        publisher: 'IANA',
        checked,
      },
      repoDoc(
        'docs/06-security-and-access.md#2-network-exposure',
        '06 Security and access, §2 Network exposure',
        checked
      ),
      repoDoc('docs/08-operations.md#7-firewall', '08 Operations, §7 Firewall', checked),
    ],
    related: ['system.panelNetworks', 'wizard.portsAuto'],
  },

  'system.panelNetworks': {
    title: 'Panel networks (panel answers on)',
    summary:
      "This machine's own addresses on which the panel answers, besides localhost. Empty means the same ranges as the game ports: the panel is reachable over the VPN and on the machine itself.",
    body: [
      {
        p: 'The panel listens on every network adapter (port 8765 by default) but accepts a connection only when it arrives at localhost or at an address inside one of these ranges. It does not bind to the VPN address itself, because Windows may start the VPN adapter after GameWarden; the filter works whenever the adapter comes up.',
      },
      {
        p: "These are this machine's local addresses, while the game-port ranges are your friends' remote ones. The panel's firewall rule, GameWarden:panel:8765/tcp, allows the same ranges.",
      },
      {
        list: [
          'Radmin VPN or Tailscale: leave it empty.',
          'To open the panel from your home network, add your LAN range, such as 192.168.1.0/24. Everyone on that network then reaches the sign-in page.',
          'The panel uses plain HTTP and relies on the VPN to encrypt the traffic (ADR-0005): avoid networks you do not trust.',
        ],
      },
      {
        warning:
          'GameWarden refuses a change that would lock out the browser you use: keep a range that contains the address you opened the panel at, or make the change on the GameWarden machine itself.',
      },
    ],
    tested:
      '0.2.0-rc.1 on the template VM, 2026-10-28: with the defaults, the panel opened over Radmin VPN from another PC and from a phone; 0.2.0-alpha.4 passed the network settings check (docs/10 §8.2).',
    verified: true,
    sources: [
      repoDoc(
        'docs/06-security-and-access.md#2-network-exposure',
        '06 Security and access, §2 Network exposure',
        checked
      ),
      repoDoc('docs/08-operations.md#7-firewall', '08 Operations, §7 Firewall', checked),
      repoDoc(
        'decisions/0005-http-only-over-vpn.md',
        'ADR-0005: Panel uses HTTP only, reachable only over VPN',
        checked
      ),
      repoDoc('docs/09-open-questions.md', '09 Open questions, OQ-28', checked),
    ],
    related: ['system.vpnRanges'],
  },

  'system.channel': {
    title: 'Update channel',
    summary:
      'Which GitHub releases Check for updates looks at: full releases only, or also release candidates and previews. Releases is the safe choice.',
    body: [
      {
        p: 'A release candidate (vX.Y.Z-rc.N) means a milestone is complete but still under test; a preview (vX.Y.Z-alpha.N) is a milestone in progress. GitHub counts both as pre-releases, which its latest release leaves out.',
      },
      {
        p: 'Both channels update the same way: GameWarden refuses a package whose signed SHA256SUMS does not match. Core restarts for a few seconds, game servers keep running, and if the new version does not start, GameWarden switches back.',
      },
      {
        p: 'The choice is not saved: the card starts on Releases, and the daily automatic check looks at releases only.',
      },
      {
        tip: 'Choose release candidates to help test a version before its release. If it misbehaves, switch back to the previous version from this card.',
      },
    ],
    values: [
      { value: 'Releases', text: 'The newest full release (vX.Y.Z).' },
      {
        value: 'Release candidates',
        text: 'The newest version of any kind: a release, a release candidate or a preview.',
      },
    ],
    tested:
      'Template VM, 2026-10-28: 0.1.0-rc.1 → rc.2 with a verified signature, back with gamewarden version use and forward again, Valheim running throughout; later previews were updated from the panel (docs/10 §8.2).',
    verified: true,
    sources: [
      repoDoc(
        'docs/10-build-test-release.md#4-versioning-and-release',
        '10 Build, test, release, §4 Versioning and release',
        checked
      ),
      repoDoc(
        'docs/10-build-test-release.md#6-self-update-from-github-releases',
        '10 Build, test, release, §6 Self-update from GitHub Releases',
        checked
      ),
      repoDoc(
        'decisions/0013-own-setup-and-update-engine.md',
        'ADR-0013: Own setup and update engine; signed update manifest',
        checked
      ),
      {
        title: 'REST API endpoints for releases: Get the latest release',
        url: 'https://docs.github.com/en/rest/releases/releases#get-the-latest-release',
        publisher: 'GitHub Docs',
        checked,
      },
      {
        title: 'Semantic Versioning 2.0.0, item 9 (pre-release versions)',
        url: 'https://semver.org/#spec-item-9',
        publisher: 'semver.org',
        checked,
      },
    ],
    related: ['system.restoreDb'],
  },

  'system.restoreDb': {
    title: 'Restore the database when switching back',
    summary:
      'Switching back can also put back the copy of the database taken just before the update to the current version. Leave it off unless the older version cannot read the database.',
    body: [
      {
        p: "Before each update GameWarden copies its database (db-before.db, made with SQLite's VACUUM INTO). The database holds the servers and their settings, the Owner account, jobs and activity. Game worlds, secrets and the network settings live elsewhere and are not touched.",
      },
      {
        p: 'Off, the older version starts with the current database. That is right unless the newer version changed the database in a way the older one cannot read.',
      },
      {
        warning:
          'On, everything stored in the database since that update is lost: servers created, settings and password changes, jobs and activity.',
      },
      { p: 'The option appears only when that copy exists.' },
    ],
    values: [
      { value: 'Off', text: 'Switch the program back; keep the current database.' },
      { value: 'On', text: 'Also put back the database copy from before the update.' },
    ],
    verified: true,
    sources: [
      repoDoc(
        'docs/10-build-test-release.md#6-self-update-from-github-releases',
        '10 Build, test, release, §6 Self-update from GitHub Releases',
        checked
      ),
      repoDoc(
        'decisions/0013-own-setup-and-update-engine.md',
        'ADR-0013: Own setup and update engine; signed update manifest',
        checked
      ),
      {
        title: 'VACUUM, §2.1 VACUUM with an INTO clause',
        url: 'https://www.sqlite.org/lang_vacuum.html#vacuuminto',
        publisher: 'SQLite',
        checked,
      },
    ],
    related: ['system.channel'],
  },
} satisfies Record<string, HelpTopic>;
