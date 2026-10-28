import { isSessionGone, safeRedirect } from './session';

describe('session', () => {
  it('goes back only to a page of this panel after signing in', () => {
    expect(safeRedirect('/servers/valheim-main/console')).toBe('/servers/valheim-main/console');
    expect(safeRedirect(undefined)).toBe('/');
    expect(safeRedirect('https://evil.example/')).toBe('/');
    expect(safeRedirect('//evil.example/')).toBe('/');
  });

  it('treats an ended session as such, but not a wrong password', () => {
    expect(isSessionGone({ status: 401, code: 'unauthenticated', title: 'Sign in first.' })).toBe(
      true
    );
    expect(isSessionGone({ status: 401, code: 'invalid_credentials', title: 'Wrong.' })).toBe(
      false
    );
    expect(isSessionGone({ status: 403, code: 'csrf_failed', title: 'Reload.' })).toBe(false);
  });
});
