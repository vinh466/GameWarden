import type { HelpTopic } from '@/components/help/types';
import { repoDoc } from './sources';

// The first-run setup link. Sources checked 2026-10-29.
const checked = '2026-10-29';

export const setupTopics = {
  'setup.token': {
    title: 'Setup link',
    summary:
      'The setup link carries a one-time token, so that only someone at the GameWarden machine can create the Owner account. Get it with gamewarden setup-url, as Administrator on that machine.',
    body: [
      {
        p: 'Core writes the token while no account exists, next to the CLI token in C:\\ProgramData\\GameWarden. The installer opens the link when it finishes; gamewarden setup-url prints it again.',
      },
      {
        list: [
          'Open it on the machine itself: the Owner can be created only from localhost, never over the VPN.',
          'It works once: creating the Owner deletes the token, and setup-url then answers that the Owner exists.',
          'A wrong or used link is refused; run gamewarden setup-url for a new one.',
        ],
      },
      {
        p: 'These limits matter because the Owner manages every server and update.',
      },
    ],
    tested:
      'Template VM, 2026-10-28 (0.2.0-alpha.1, docs/10 §8.3): the link opened over Radmin was refused, the same link on the machine created the Owner, and setup-url then answered that the Owner exists.',
    verified: true,
    sources: [
      repoDoc(
        'docs/06-security-and-access.md#4-authentication',
        '06 Security and access, §4 Authentication',
        checked
      ),
      repoDoc(
        'docs/10-build-test-release.md#5-first-install-on-the-vm',
        '10 Build, test, release, §5 First install on the VM',
        checked
      ),
      repoDoc(
        'docs/10-build-test-release.md#83-walk-through-for-panel-access-m2-phase-2',
        '10 Build, test, release, §8.3 Walk-through for panel access',
        checked
      ),
    ],
    related: ['auth.password'],
  },
} satisfies Record<string, HelpTopic>;
