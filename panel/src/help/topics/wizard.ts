import type { HelpTopic } from '@/components/help/types';
import { repoDoc } from './sources';

// The new server wizard. Sources checked 2026-10-29.
const checked = '2026-10-29';

export const wizardTopics = {
  'wizard.id': {
    title: 'Server id',
    summary:
      "The server's permanent short name: 2 to 63 lowercase letters, digits and dashes, starting with a letter or a digit. It cannot be changed later.",
    body: [
      {
        p: 'GameWarden uses the id wherever a name has to be safe for files and commands:',
      },
      {
        list: [
          "the server's folder, instances\\<id> in the data root (C:\\GameWarden by default);",
          'its firewall rules, GameWarden:<id>:<port>/<protocol>;',
          'the command line, such as gamewarden start <id> or gamewarden logs <id>.',
        ],
      },
      {
        p: 'The wizard fills it in from the server name (accents dropped, spaces turned into dashes) until you edit it yourself. Two servers cannot have the same id.',
      },
      {
        tip: 'Name it after the game and its purpose, such as valheim-main or valheim-test, and put the rest in the server name.',
      },
    ],
    verified: true,
    sources: [
      repoDoc('docs/03-domain-model.md#1-entities', '03 Domain model, §1 Entities', checked),
      repoDoc(
        'docs/08-operations.md#1-filesystem-layout',
        '08 Operations, §1 Filesystem layout',
        checked
      ),
      repoDoc('docs/08-operations.md#7-firewall', '08 Operations, §7 Firewall', checked),
    ],
    related: ['wizard.name'],
  },

  'wizard.name': {
    title: 'Server name',
    summary:
      'The name GameWarden shows for this server in the panel: any text up to 80 characters, with spaces and accents.',
    body: [
      {
        p: 'It is a label only. Folders, firewall rules and commands use the id, which the wizard fills in from the name until you edit the id.',
      },
      {
        p: "The name players see in the game's own server list is a setting of the game, such as Valheim's Name in Valheim, in the next sections of the wizard.",
      },
    ],
    verified: true,
    sources: [
      repoDoc('docs/03-domain-model.md#1-entities', '03 Domain model, §1 Entities', checked),
      repoDoc(
        'adapters/valheim/settings.schema.json',
        "Valheim adapter's settings (serverName)",
        checked
      ),
    ],
    related: ['wizard.id'],
  },

  'wizard.portsAuto': {
    title: 'Ports',
    summary:
      "GameWarden picks the server's network ports for you: the game's defaults, or all of them moved by 10 while another server or program uses one.",
    body: [
      {
        p: 'For Valheim the defaults are 2456 and 2457 (UDP); a second Valheim server gets 2466 and 2467, a third 2476 and 2477. Friends join at the VPN address of this machine and the first port, such as 26.1.2.3:2456.',
      },
      {
        p: 'Each port gets a Windows firewall rule, GameWarden:<id>:<port>/<protocol>, open only to the VPN ranges. The rules are removed with the server.',
      },
      {
        p: 'Before each start GameWarden checks that no other program listens on the ports, and does not start the server if one does.',
      },
    ],
    tested:
      '0.1.0-rc.1 on the template VM, 2026-10-28: the rules GameWarden:valheim-main:2456/udp and :2457/udp were limited to the VPN ranges, and a friend joined over Radmin VPN (docs/10 §8.1–8.2).',
    verified: true,
    sources: [
      repoDoc('docs/08-operations.md#7-firewall', '08 Operations, §7 Firewall', checked),
      repoDoc(
        'docs/06-security-and-access.md#2-network-exposure',
        '06 Security and access, §2 Network exposure',
        checked
      ),
      repoDoc(
        'docs/10-build-test-release.md#82-results',
        '10 Build, test, release, §8.2 Results',
        checked
      ),
    ],
    related: ['system.vpnRanges'],
  },
} satisfies Record<string, HelpTopic>;
