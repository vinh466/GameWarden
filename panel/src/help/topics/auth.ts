import type { HelpTopic } from '@/components/help/types';
import { repoDoc } from './sources';

// The Owner's password (setup step 1, change password). Sources checked 2026-10-29.
const checked = '2026-10-29';

export const authTopics = {
  'auth.password': {
    title: 'Password',
    summary:
      'At least 10 characters and no other rules: length is what makes a password hard to guess. 15 or more is better, because it is the only thing that protects the account.',
    body: [
      {
        p: 'GameWarden accepts 10 to 256 characters and does not ask for digits, symbols or capitals. NIST guidance (SP 800-63B-4, 2025) also rules out such composition rules, and asks for at least 15 characters when a password is the only factor, as it is here.',
      },
      {
        list: [
          'Use a password you do not use anywhere else.',
          'Five failed sign-ins for one user name lock it for 15 minutes.',
          'Changing the password signs out every other device; this one stays signed in.',
          'Forgot it? On the GameWarden machine, as Administrator: gamewarden owner reset-password.',
        ],
      },
      {
        p: 'The panel uses plain HTTP inside the VPN (ADR-0005): on the way, the password is protected by the VPN only.',
      },
    ],
    tested:
      'Template VM, 2026-10-28 (0.2.0-alpha.1, docs/10 §8.3): Owner created with a 10-character password, five wrong passwords locked the sign-in, a password change signed out the other device.',
    verified: true,
    sources: [
      repoDoc(
        'docs/06-security-and-access.md#4-authentication',
        '06 Security and access, §4 Authentication',
        checked
      ),
      {
        title:
          'SP 800-63B-4: Authentication and Authenticator Management, §3.1.1.2 Password verifiers',
        url: 'https://pages.nist.gov/800-63-4/sp800-63b.html',
        publisher: 'NIST',
        checked,
      },
      repoDoc(
        'decisions/0005-http-only-over-vpn.md',
        'ADR-0005: Panel uses HTTP only, reachable only over VPN',
        checked
      ),
    ],
  },
} satisfies Record<string, HelpTopic>;
