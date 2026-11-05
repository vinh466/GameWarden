import { useSyncExternalStore } from 'react';

// One update at a time, shared by the header, the System card and the banner (docs/11 §2 System, §6.1). The state is
// kept outside React so the realtime layer can see it, and in localStorage so a reload during the Core restart still
// knows what is happening.

export type UpdatePhase = 'idle' | 'preparing' | 'restarting' | 'succeeded' | 'failed';

export interface UpdateTarget {
  from: string;
  to: string;
  notes: string | null;
  url: string | null;
  publishedAt: string | null;
}

export interface UpdateState {
  phase: UpdatePhase;
  target: UpdateTarget | null;
  startedAt: number | null;
  /** When the current phase began; the banner uses it for the "still working" warning. */
  phaseAt: number | null;
  error: string | null;
  bannerDismissed: boolean;
  dialogOpen: boolean;
  notesOpen: boolean;
}

const StorageKey = 'gamewarden.update';
const ReloadedKey = 'gamewarden.update.reloaded';

const initial: UpdateState = {
  phase: 'idle',
  target: null,
  startedAt: null,
  phaseAt: null,
  error: null,
  bannerDismissed: false,
  dialogOpen: false,
  notesOpen: false,
};

let state: UpdateState = load();

const listeners = new Set<() => void>();

function load(): UpdateState {
  if (typeof window === 'undefined') {
    return initial;
  }

  try {
    const raw = window.localStorage.getItem(StorageKey);
    if (!raw) {
      return initial;
    }

    const saved = JSON.parse(raw) as Partial<UpdateState>;
    if (!saved.target || !saved.phase || saved.phase === 'idle') {
      return initial;
    }

    return {
      ...initial,
      ...saved,
      // UI flags never survive a reload; only the update itself does.
      dialogOpen: false,
      notesOpen: false,
    };
  } catch {
    return initial;
  }
}

function persist() {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    if (state.phase === 'idle') {
      window.localStorage.removeItem(StorageKey);
      return;
    }

    window.localStorage.setItem(
      StorageKey,
      JSON.stringify({ ...state, dialogOpen: false, notesOpen: false })
    );
  } catch {
    // A browser with storage disabled still gets the in-memory flow.
  }
}

function emit() {
  for (const listener of listeners) {
    listener();
  }
}

export function getUpdateState() {
  return state;
}

export function subscribeUpdateState(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function useUpdateState() {
  return useSyncExternalStore(subscribeUpdateState, getUpdateState, getUpdateState);
}

function setState(next: UpdateState) {
  if (next === state) {
    return;
  }

  state = next;
  persist();
  emit();
}

export function isUpdateActive(value: UpdateState = state) {
  return value.phase === 'preparing' || value.phase === 'restarting';
}

/** The banner owns the screen while an update runs; LinkLostBanner steps aside for it. */
export function isUpdateBannerVisible(value: UpdateState = state) {
  return value.phase !== 'idle' && !value.bannerDismissed;
}

export function beginUpdate(target: UpdateTarget) {
  if (typeof window !== 'undefined') {
    window.sessionStorage.removeItem(ReloadedKey);
  }

  setState({
    ...initial,
    phase: 'preparing',
    target,
    startedAt: Date.now(),
    phaseAt: Date.now(),
  });
}

export function markRestarting() {
  setState(
    state.phase === 'preparing' ? { ...state, phase: 'restarting', phaseAt: Date.now() } : state
  );
}

export function markSucceeded() {
  if (!state.target || state.phase === 'succeeded') {
    return;
  }

  setState({
    ...state,
    phase: 'succeeded',
    phaseAt: Date.now(),
    error: null,
    bannerDismissed: false,
  });
}

export function markFailed(error: string) {
  if (!state.target) {
    return;
  }

  setState({ ...state, phase: 'failed', phaseAt: Date.now(), error, bannerDismissed: false });
}

export function openUpdateDialog() {
  if (isUpdateActive()) {
    return;
  }

  setState({ ...state, dialogOpen: true, notesOpen: false });
}

export function closeUpdateDialog() {
  setState({ ...state, dialogOpen: false });
}

export function openUpdateNotes() {
  setState({ ...state, notesOpen: true, dialogOpen: false });
}

export function closeUpdateNotes() {
  setState({ ...state, notesOpen: false });
}

export function dismissUpdateBanner() {
  if (state.phase === 'failed') {
    setState(initial);
    return;
  }

  setState({ ...state, bannerDismissed: true });
}

/** Success stays in the store so the System card can still offer its release notes after the banner is closed. */
export function clearUpdate() {
  setState(initial);
}

export function hasReloadedInto(target: string) {
  return typeof window !== 'undefined' && window.sessionStorage.getItem(ReloadedKey) === target;
}

export function markReloadedInto(target: string) {
  if (typeof window !== 'undefined') {
    window.sessionStorage.setItem(ReloadedKey, target);
  }
}

/** Suppress the generic "Core restarted / new version" toast while the panel is showing its own update banner. */
export function shouldSuppressCoreNotice(version?: string) {
  if (state.phase === 'preparing' || state.phase === 'restarting' || state.phase === 'failed') {
    return true;
  }

  // The success banner and the realtime event can race: the system poll may notice the new version first. Suppress
  // only that update's own event, not a later restart of the same version.
  return (
    state.phase === 'succeeded' &&
    !!version &&
    version === state.target?.to &&
    state.phaseAt != null &&
    Date.now() - state.phaseAt < 60_000
  );
}

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key !== StorageKey) {
      return;
    }

    const next = load();
    if (next !== state) {
      state = { ...next, dialogOpen: state.dialogOpen, notesOpen: state.notesOpen };
      emit();
    }
  });
}
