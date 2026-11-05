import {
  beginUpdate,
  clearUpdate,
  dismissUpdateBanner,
  getUpdateState,
  hasReloadedInto,
  isUpdateActive,
  isUpdateBannerVisible,
  markFailed,
  markReloadedInto,
  markRestarting,
  markSucceeded,
  openUpdateDialog,
  openUpdateNotes,
  shouldSuppressCoreNotice,
} from './updateStore';

const target = {
  from: '0.3.0-dev.8',
  to: '0.3.0-dev.9',
  notes: '## Data impact\n\nNothing to migrate.',
  url: 'https://github.com/vinh466/GameWarden/releases/tag/v0.3.0-dev.9',
  publishedAt: '2026-11-05T10:00:00Z',
};

describe('updateStore', () => {
  beforeEach(() => {
    clearUpdate();
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it('walks from preparing to restarting and keeps the flow active', () => {
    beginUpdate(target);
    expect(getUpdateState().phase).toBe('preparing');
    expect(getUpdateState().target?.to).toBe(target.to);
    expect(isUpdateActive()).toBe(true);
    expect(isUpdateBannerVisible()).toBe(true);
    expect(window.localStorage.getItem('gamewarden.update')).toContain('preparing');

    markRestarting();
    expect(getUpdateState().phase).toBe('restarting');
    expect(isUpdateBannerVisible()).toBe(true);
  });

  it('keeps the notes after success so the System card can still show them', () => {
    beginUpdate(target);
    markRestarting();
    markSucceeded();
    expect(getUpdateState().phase).toBe('succeeded');
    expect(shouldSuppressCoreNotice()).toBe(false);
    expect(shouldSuppressCoreNotice(target.to)).toBe(true);
    expect(shouldSuppressCoreNotice('0.3.0-dev.10')).toBe(false);

    dismissUpdateBanner();
    expect(getUpdateState().phase).toBe('succeeded');
    expect(getUpdateState().bannerDismissed).toBe(true);
    expect(isUpdateBannerVisible()).toBe(false);

    openUpdateNotes();
    expect(getUpdateState().notesOpen).toBe(true);
  });

  it('suppresses the generic Core toast during an update and clears a dismissed failure', () => {
    beginUpdate(target);
    markFailed('Could not download the package.');
    expect(getUpdateState().phase).toBe('failed');
    expect(shouldSuppressCoreNotice()).toBe(true);

    dismissUpdateBanner();
    expect(getUpdateState().phase).toBe('idle');
    expect(shouldSuppressCoreNotice()).toBe(false);
  });

  it('opens the dialog only when no update is running', () => {
    beginUpdate(target);
    openUpdateDialog();
    expect(getUpdateState().dialogOpen).toBe(false);

    clearUpdate();
    openUpdateDialog();
    expect(getUpdateState().dialogOpen).toBe(true);
  });

  it('remembers a reload so the success banner stops offering the same reload', () => {
    expect(hasReloadedInto(target.to)).toBe(false);
    markReloadedInto(target.to);
    expect(hasReloadedInto(target.to)).toBe(true);
  });
});
