import type { QueryClient, QueryKey } from '@tanstack/react-query';
import type { ActivityItem, ConsoleTail, InstanceSnapshot, JobInfo, OutputLine } from './generated';
import { queries } from './queries';

// How realtime events (docs/05 §3) change the TanStack Query cache: the same data REST would return, without a
// round trip. Lists keep their filter: a job of another server does not enter a server's job list.

export const CONSOLE_LINES = 1000;

export function upsertById<T extends { id: string | number }>(list: T[], item: T): T[] {
  const index = list.findIndex((existing) => existing.id === item.id);
  return index < 0 ? [item, ...list] : list.map((existing, i) => (i === index ? item : existing));
}

export function appendConsole(tail: ConsoleTail, lines: OutputLine[]): ConsoleTail {
  return { lines: [...tail.lines, ...lines].slice(-CONSOLE_LINES) };
}

/** The `query` part of a generated key (`[{ _id, baseUrl, query? }]`). */
function filterOf(key: QueryKey): { instanceId?: string } {
  return ((key[0] as { query?: { instanceId?: string } } | undefined)?.query ?? {}) as {
    instanceId?: string;
  };
}

/** Every cached list of one operation (all jobs, one server's jobs, …) that the item belongs in. */
function updateLists<T extends { id: string | number; instanceId: string | null }>(
  queryClient: QueryClient,
  operation: string,
  item: T
) {
  for (const [key, list] of queryClient.getQueriesData<T[]>({ queryKey: [{ _id: operation }] })) {
    const { instanceId } = filterOf(key);
    if (list && (!instanceId || instanceId === item.instanceId)) {
      queryClient.setQueryData<T[]>(key, upsertById(list, item));
    }
  }
}

export function applyInstanceChanged(queryClient: QueryClient, instance: InstanceSnapshot) {
  queryClient.setQueryData<InstanceSnapshot[]>(queries.instances().queryKey, (list) =>
    list ? upsertById(list, instance).sort((a, b) => a.id.localeCompare(b.id)) : list
  );
  queryClient.setQueryData(queries.instance(instance.id).queryKey, instance);
}

export function applyInstanceRemoved(queryClient: QueryClient, instanceId: string) {
  queryClient.setQueryData<InstanceSnapshot[]>(queries.instances().queryKey, (list) =>
    list?.filter((instance) => instance.id !== instanceId)
  );
  queryClient.removeQueries({ queryKey: queries.instance(instanceId).queryKey });
}

export function applyJobUpdated(queryClient: QueryClient, job: JobInfo) {
  queryClient.setQueryData(queries.job(job.id).queryKey, job);
  updateLists(queryClient, 'listJobs', job);
}

export function applyActivityAdded(queryClient: QueryClient, item: ActivityItem) {
  updateLists(queryClient, 'listActivity', item);
}

export function applyConsoleLines(
  queryClient: QueryClient,
  instanceId: string,
  lines: OutputLine[]
) {
  queryClient.setQueryData<ConsoleTail>(queries.console(instanceId).queryKey, (tail) =>
    tail ? appendConsole(tail, lines) : tail
  );
}
