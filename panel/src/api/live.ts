import { realtimeStatus } from './realtimeStatus';

// How fresh live data is kept. With the realtime link (docs/05 §3) events update the query cache and polling is only a
// safety net; without it (mock API, a dropped link) queries that show changing state poll.
export const live = {
  /** Instances, jobs, the console tail, activity. */
  fast: () => (realtimeStatus() === 'connected' ? 30_000 : 2_000),
  /** System status: version, resources, prerequisites. */
  slow: 30_000,
} as const;
