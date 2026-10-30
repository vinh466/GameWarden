import {
  checkForUpdateOptions,
  getAdapterHelpOptions,
  getConsoleTailOptions,
  getInstanceModsConfigContentOptions,
  getInstanceModsLogOptions,
  getInstanceModsOptions,
  getInstanceOptions,
  getInstanceSettingsOptions,
  getJobLogOptions,
  getJobOptions,
  getMetricsOptions,
  getSystemOptions,
  getSystemSettingsOptions,
  listActivityOptions,
  listAdaptersOptions,
  listInstancesOptions,
  listInstanceModsConfigsOptions,
  listJobsOptions,
  listVersionsOptions,
  searchInstanceModsOptions,
} from './generated/@tanstack/react-query.gen';
import { live } from './live';

// Every read the panel makes: the generated query options plus how fresh each must be (docs/11 §10 rule 3).
export const queries = {
  system: () => ({ ...getSystemOptions(), refetchInterval: live.slow }),
  updateCheck: (prerelease: boolean) => checkForUpdateOptions({ query: { prerelease } }),
  systemSettings: () => getSystemSettingsOptions(),
  versions: () => listVersionsOptions(),
  adapters: () => listAdaptersOptions(),
  // Help topics change only with the adapter: one fetch per adapter, on the first help button opened (docs/12 §5.1).
  adapterHelp: (id: string) => ({
    ...getAdapterHelpOptions({ path: { id } }),
    staleTime: 30 * 60_000,
  }),
  instances: () => ({ ...listInstancesOptions(), refetchInterval: live.fast }),
  instance: (id: string) => ({
    ...getInstanceOptions({ path: { id } }),
    refetchInterval: live.fast,
  }),
  settings: (id: string) => getInstanceSettingsOptions({ path: { id } }),
  console: (id: string) => ({
    ...getConsoleTailOptions({ path: { id }, query: { tail: 1000 } }),
    refetchInterval: live.fast,
  }),
  jobs: (instanceId?: string) => ({
    ...listJobsOptions({ query: instanceId ? { instanceId } : {} }),
    refetchInterval: live.fast,
  }),
  job: (jobId: string) => ({ ...getJobOptions({ path: { jobId } }), refetchInterval: live.fast }),
  jobLog: (jobId: string) => ({
    ...getJobLogOptions({ path: { jobId }, query: { tail: 2000 } }),
    refetchInterval: live.fast,
  }),
  // Mods (docs/13 §4.10): a building set is followed closely; the job's events do not carry the set.
  mods: (id: string) => ({
    ...getInstanceModsOptions({ path: { id } }),
    refetchInterval: (query: { state: { data?: { pending: { state: string } | null } } }) =>
      query.state.data?.pending?.state === 'building' ? 2_000 : live.fast(),
  }),
  modConfigs: (id: string) => listInstanceModsConfigsOptions({ path: { id } }),
  modConfig: (id: string, path: string) =>
    getInstanceModsConfigContentOptions({ path: { id }, query: { path } }),
  // Core caches Thunderstore's list for a day; a search that was made stays good for a few minutes.
  modSearch: (id: string, q: string) => ({
    ...searchInstanceModsOptions({ path: { id }, query: { q, limit: 20 } }),
    staleTime: 5 * 60_000,
  }),
  modLog: (id: string) => getInstanceModsLogOptions({ path: { id }, query: { tail: 500 } }),
  metrics: () => ({ ...getMetricsOptions(), refetchInterval: live.metrics }),
  activity: (instanceId?: string) => ({
    ...listActivityOptions({ query: instanceId ? { instanceId, limit: 100 } : { limit: 100 } }),
    refetchInterval: live.fast,
  }),
};
