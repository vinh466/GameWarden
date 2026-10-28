import { useSyncExternalStore } from 'react';

/** `off`: no realtime link (mock API, signed out); the panel then polls. */
export type RealtimeStatus = 'off' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

let status: RealtimeStatus = 'off';
const listeners = new Set<() => void>();

export function setRealtimeStatus(next: RealtimeStatus) {
  status = next;
  listeners.forEach((listener) => listener());
}

export const realtimeStatus = () => status;

export function useRealtimeStatus() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => status
  );
}
