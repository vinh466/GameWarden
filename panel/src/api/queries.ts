import {
  checkForUpdateOptions,
  getConsoleTailOptions,
  getInstanceOptions,
  getInstanceSettingsOptions,
  getJobLogOptions,
  getJobOptions,
  getSystemOptions,
  listActivityOptions,
  listAdaptersOptions,
  listInstancesOptions,
  listJobsOptions,
} from './generated/@tanstack/react-query.gen';
import { live } from './live';

// Every read the panel makes: the generated query options plus how fresh each must be (docs/11 §10 rule 3).
export const queries = {
  system: () => ({ ...getSystemOptions(), refetchInterval: live.slow }),
  updateCheck: (prerelease: boolean) => checkForUpdateOptions({ query: { prerelease } }),
  adapters: () => listAdaptersOptions(),
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
  activity: (instanceId?: string) => ({
    ...listActivityOptions({ query: instanceId ? { instanceId, limit: 100 } : { limit: 100 } }),
    refetchInterval: live.fast,
  }),
};
