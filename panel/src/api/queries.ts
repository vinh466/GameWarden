import {
  checkForUpdateOptions,
  getAdapterHelpOptions,
  getConsoleTailOptions,
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
  listJobsOptions,
  listVersionsOptions,
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
  metrics: () => ({ ...getMetricsOptions(), refetchInterval: live.metrics }),
  activity: (instanceId?: string) => ({
    ...listActivityOptions({ query: instanceId ? { instanceId, limit: 100 } : { limit: 100 } }),
    refetchInterval: live.fast,
  }),
};
