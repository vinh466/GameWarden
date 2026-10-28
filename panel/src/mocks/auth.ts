import { http, HttpResponse } from 'msw/http';
import type {
  ChangePasswordRequest,
  CreateOwnerRequest,
  LoginRequest,
  SessionInfo,
} from '@/api/generated';

// Sign-in for `npm run dev:mock`. Signed in by default; for the other screens set localStorage
// `gamewarden.mock.auth` to `signed-out` (sign-in page) or `setup` (first-run setup) and reload.
// The mock Owner is `owner` with the password `correct horse battery`.

const mode = (() => {
  try {
    return localStorage.getItem('gamewarden.mock.auth');
  } catch {
    return null;
  }
})();

const state = {
  ownerExists: mode !== 'setup',
  password: 'correct horse battery',
  session: mode === 'signed-out' || mode === 'setup' ? null : newSession('owner'),
};

function newSession(userName: string): SessionInfo {
  return { userName, isOwner: true, csrfToken: crypto.randomUUID() };
}

const problem = (status: number, code: string, title: string) =>
  HttpResponse.json(
    { status, title, code },
    { status, headers: { 'Content-Type': 'application/problem+json' } }
  );

export const authHandlers = [
  http.get('/api/v1/auth/me', () =>
    state.session
      ? HttpResponse.json(state.session)
      : problem(401, 'unauthenticated', 'Sign in first.')
  ),
  http.post('/api/v1/auth/login', async ({ request }) => {
    const body = (await request.json()) as LoginRequest;
    if (
      !state.ownerExists ||
      body.userName.toLowerCase() !== 'owner' ||
      body.password !== state.password
    ) {
      return problem(401, 'invalid_credentials', 'Wrong user name or password.');
    }

    state.session = newSession('owner');
    return HttpResponse.json(state.session);
  }),
  http.post('/api/v1/auth/logout', () => {
    state.session = null;
    return new HttpResponse(null, { status: 204 });
  }),
  http.post('/api/v1/auth/password', async ({ request }) => {
    const body = (await request.json()) as ChangePasswordRequest;
    if (body.currentPassword !== state.password) {
      return problem(400, 'invalid_credentials', 'The current password is wrong.');
    }

    state.password = body.newPassword;
    state.session = newSession('owner');
    return HttpResponse.json(state.session);
  }),
  http.get('/api/v1/setup/status', () => HttpResponse.json({ needsSetup: !state.ownerExists })),
  http.post('/api/v1/setup/owner', async ({ request }) => {
    const body = (await request.json()) as CreateOwnerRequest;
    if (state.ownerExists) {
      return problem(409, 'already_set_up', 'The Owner exists already; sign in.');
    }

    if (!body.setupToken) {
      return problem(403, 'invalid_setup_token', 'The setup link is wrong or used.');
    }

    state.ownerExists = true;
    state.password = body.password;
    state.session = newSession(body.userName);
    return HttpResponse.json(state.session);
  }),
];
