import type { ActivityItem, InstanceSnapshot, JobInfo, SystemInfo } from '@/api/generated';

export type AttentionItem =
  | { kind: 'core'; at: string; message: string }
  | { kind: 'server'; server: InstanceSnapshot }
  | { kind: 'job'; job: JobInfo }
  | { kind: 'prerequisite'; id: string; neededBy: string[] }
  | { kind: 'dns'; host: string; purpose: string; status: string };

const DAY_MS = 24 * 60 * 60 * 1000;

export function failedRecently(job: JobInfo, now: number) {
  return job.state === 'failed' && now - Date.parse(job.finishedAt ?? job.createdAt) < DAY_MS;
}

/**
 * What the Owner should look at, from data the panel already has (docs/11 §4 Dashboard): Core restarted after a
 * failure (docs/07 §8), servers that crashed or stopped with a problem, jobs that failed in the last 24 hours,
 * missing prerequisites and blocked download hosts.
 */
export function attentionItems(
  instances: InstanceSnapshot[],
  jobs: JobInfo[],
  system: SystemInfo,
  now = Date.now(),
  activity: ActivityItem[] = []
): AttentionItem[] {
  // Only the latest start counts: a clean restart since then means it is over.
  const lastStart = activity.find((item) => item.kind === 'core.started');
  const recovered =
    lastStart?.level === 'warning' && now - Date.parse(lastStart.at) < DAY_MS
      ? [{ kind: 'core' as const, at: lastStart.at, message: lastStart.message }]
      : [];
  return [
    ...recovered,
    ...instances
      .filter(
        (server) =>
          server.state === 'crashed' ||
          server.state === 'faulted' ||
          (server.state !== 'running' && !!server.lastProblem)
      )
      .map((server) => ({ kind: 'server' as const, server })),
    ...jobs.filter((job) => failedRecently(job, now)).map((job) => ({ kind: 'job' as const, job })),
    ...system.prerequisites
      .filter((prerequisite) => !prerequisite.installed && prerequisite.neededBy.length > 0)
      .map(({ id, neededBy }) => ({ kind: 'prerequisite' as const, id, neededBy })),
    ...system.network
      .filter((host) => host.status !== 'ok')
      .map(({ host, purpose, status }) => ({ kind: 'dns' as const, host, purpose, status })),
  ];
}
