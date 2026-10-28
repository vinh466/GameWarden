import type { QueryClient } from '@tanstack/react-query';
import type { SessionInfo } from './generated';
import {
  getSessionOptions,
  getSessionQueryKey,
  getSetupStatusOptions,
} from './generated/@tanstack/react-query.gen';
import { client } from './generated/client.gen';
import { toProblem } from './problems';

// The signed-in user (docs/05 §2 "Auth and session"): asked once, then kept until sign-in, sign-out or a 401.
export const sessionQuery = () => ({ ...getSessionOptions(), staleTime: Infinity, retry: false });

export const setupStatusQuery = () => ({ ...getSetupStatusOptions(), retry: false });

export const sessionKey = () => getSessionQueryKey();

/** docs/05 §1: every change made with the session carries the CSRF token Core issued with it. */
export function sendCsrfToken(queryClient: QueryClient) {
  client.interceptors.request.use((request) => {
    const session = queryClient.getQueryData<SessionInfo>(getSessionQueryKey());
    if (session && !['GET', 'HEAD', 'OPTIONS'].includes(request.method)) {
      request.headers.set('X-GW-CSRF', session.csrfToken);
    }

    return request;
  });
}

/** The session ended (expired, signed out elsewhere, password changed): not a wrong password on the sign-in form. */
export function isSessionGone(error: unknown) {
  const problem = toProblem(error, '');
  return problem.status === 401 && problem.code === 'unauthenticated';
}

/** After signing in, go back where the user was, but only to a page of this panel. */
export function safeRedirect(path: string | undefined) {
  return path && path.startsWith('/') && !path.startsWith('//') ? path : '/';
}
