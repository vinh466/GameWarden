import {
  checkForUpdateOptions,
  getAdapterHelpOptions,
  getAgentHostOptions,
  getAgentSettingsOptions,
  getConsoleTailOptions,
  getInstanceIntegrationOptions,
  getInstanceModsConfigContentOptions,
  getInstanceModsConfigRevisionOptions,
  getInstanceModsLogOptions,
  getInstanceModsOptions,
  getInstanceOptions,
  getInstanceSettingsOptions,
  getJobLogOptions,
  getJobOptions,
  getMetricsOptions,
  getNotificationSettingsOptions,
  getNotificationStatusPreviewOptions,
  listSystemLogComponentsOptions,
  getSystemLogSummaryOptions,
  getSystemOptions,
  getSystemSettingsOptions,
  getUpdateStatusOptions,
  listActivityOptions,
  listAdaptersOptions,
  listAgentAuditOptions,
  listAgentClientsOptions,
  listAgentPermissionsOptions,
  listAgentTokensOptions,
  listInstancesOptions,
  listInstanceModsConfigRevisionsOptions,
  listInstanceModsConfigsOptions,
  listJobsOptions,
  listNotificationDeliveriesOptions,
  listSystemLogsOptions,
  searchInstanceModsOptions,
} from './generated/@tanstack/react-query.gen';
import { live } from './live';

// Every read the panel makes: the generated query options plus how fresh each must be (docs/11 §10 rule 3).
export const queries = {
  system: () => ({ ...getSystemOptions(), refetchInterval: live.slow }),
  // The card checks once when it opens; Core's daily check still fills system.updateCheck for the header badge.
  updateCheck: () => ({ ...checkForUpdateOptions(), staleTime: 5 * 60_000 }),
  // 10 §6: the panel polls this while POST /system/update is still downloading or verifying.
  updateStatus: () => ({
    ...getUpdateStatusOptions(),
    staleTime: 0,
    refetchInterval: 1_000,
    retry: false,
  }),
  systemSettings: () => getSystemSettingsOptions(),
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
  // Game API (docs/16): players and metrics move while the tab is open, and no realtime event carries them.
  integration: (id: string) => ({
    ...getInstanceIntegrationOptions({ path: { id } }),
    refetchInterval: live.gameApi,
  }),
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
  // A file's revisions (ADR-0021); a revision never changes.
  modConfigRevisions: (id: string, path: string) =>
    listInstanceModsConfigRevisionsOptions({ path: { id }, query: { path } }),
  modConfigRevision: (id: string, revisionId: number) => ({
    ...getInstanceModsConfigRevisionOptions({ path: { id, revisionId } }),
    staleTime: Infinity,
  }),
  // Core caches Thunderstore's list for a day; a search that was made stays good for a few minutes.
  modSearch: (id: string, q: string) => ({
    ...searchInstanceModsOptions({ path: { id }, query: { q, limit: 20 } }),
    staleTime: 5 * 60_000,
  }),
  modLog: (id: string) => getInstanceModsLogOptions({ path: { id }, query: { tail: 500 } }),
  metrics: () => ({ ...getMetricsOptions(), refetchInterval: live.metrics }),
  // System log (docs/19): the page follows new rows unless paused; the summary and component list move slower.
  systemLogs: (
    filters: {
      from?: string;
      to?: string;
      level?: string;
      component?: string;
      instanceId?: string;
      jobId?: string;
      q?: string;
    },
    paused = false,
    limit = 100
  ) => ({
    ...listSystemLogsOptions({ query: { ...filters, limit } }),
    refetchInterval: paused ? undefined : live.fast(),
  }),
  systemLogSummary: (minutes = 60) => ({
    ...getSystemLogSummaryOptions({ query: { minutes } }),
    refetchInterval: live.slow,
  }),
  systemLogComponents: () => ({ ...listSystemLogComponentsOptions(), staleTime: 60_000 }),
  notificationSettings: () => getNotificationSettingsOptions(),
  notificationStatusPreview: () => ({
    ...getNotificationStatusPreviewOptions(),
    refetchInterval: live.slow,
  }),
  notificationDeliveries: () => ({
    ...listNotificationDeliveriesOptions({ query: { limit: 100 } }),
    refetchInterval: live.slow,
  }),
  activity: (instanceId?: string) => ({
    ...listActivityOptions({ query: instanceId ? { instanceId, limit: 100 } : { limit: 100 } }),
    refetchInterval: live.fast,
  }),
  // Agents (docs/15): clients and their settings change rarely; the audit is a short, newest-first page.
  agents: () => ({ ...listAgentClientsOptions(), refetchInterval: live.slow }),
  agentSettings: () => getAgentSettingsOptions(),
  agentHost: () => getAgentHostOptions(),
  agentPermissions: () => ({ ...listAgentPermissionsOptions(), staleTime: Infinity }),
  agentTokens: (id: string) => listAgentTokensOptions({ path: { id } }),
  agentAudit: (clientId?: string) => ({
    ...listAgentAuditOptions({ query: { limit: 50, ...(clientId ? { clientId } : {}) } }),
    refetchInterval: live.slow,
  }),
};
