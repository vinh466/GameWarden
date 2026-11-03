import type { HelpTopic } from '@/components/help/types';
import { repoDoc } from './sources';

// System page: network (also setup step 3) and the development update line. Sources checked 2026-11-03.
const checked = '2026-11-03';

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

  'system.update': {
    title: 'Development updates',
    summary:
      'The VM follows one development line. Each update moves forward; there is no rollback and no switch back to an older version.',
    body: [
      {
        p: 'Check asks GitHub for the newest release of any kind. The card shows that release and its notes before you confirm.',
      },
      {
        p: 'Read the notes first. They say whether data is kept, what to back up, and what to re-import. The usual backup is C:\\GameWarden\\instances (worlds, mods and configs) and C:\\GameWarden\\db\\gamewarden.db.',
      },
      {
        p: 'Game servers keep running while Core restarts. A release that does not start is fixed by installing the next build; GameWarden does not roll back automatically.',
      },
      {
        warning:
          'A development release can change the database in a way an older build cannot read. If the notes say reset, start from a clean data folder and re-import what you backed up.',
      },
    ],
    tested:
      'Template VM, 2026-11-03: the forward-only flow replaced the rc/switch-back flow of the M2 test builds (ADR-0027).',
    verified: true,
    sources: [
      repoDoc(
        'docs/10-build-test-release.md#6-self-update-from-github-releases',
        '10 Build, test, release, §6 Self-update from GitHub Releases',
        checked
      ),
      repoDoc(
        'decisions/0027-development-releases-move-forward.md',
        'ADR-0027: Development releases move forward',
        checked
      ),
      {
        title: 'REST API endpoints for releases: List releases',
        url: 'https://docs.github.com/en/rest/releases/releases#list-releases',
        publisher: 'GitHub Docs',
        checked,
      },
    ],
  },
} satisfies Record<string, HelpTopic>;
