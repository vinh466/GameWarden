// How fresh live data is kept. Until the realtime hub arrives (docs/11 §14 phase 3, docs/05 §3), queries that show
// changing state poll; afterwards SignalR events update the cache and these intervals go away.
export const live = {
  /** Instances, running jobs, the console tail. */
  fast: 2_000,
  /** System status: version, resources, prerequisites. */
  slow: 30_000,
} as const;
