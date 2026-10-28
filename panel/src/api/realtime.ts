import { useEffect } from 'react';
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr';
import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { notifications } from '@mantine/notifications';
import i18n from '@/i18n';
import type { ActivityItem, InstanceSnapshot, JobInfo, MetricPoint, OutputLine } from './generated';
import { queries } from './queries';
import {
  applyActivityAdded,
  applyConsoleLines,
  applyInstanceChanged,
  applyInstanceRemoved,
  applyJobUpdated,
  applyMetricsSampled,
} from './realtimeCache';
import {
  noticeForCore,
  noticeForInstance,
  noticeForJob,
  type CoreSeen,
  type Notice,
} from './realtimeNotices';
import { setRealtimeStatus } from './realtimeStatus';

// The realtime link (docs/05 §3, Microsoft's SignalR JavaScript client): events update the query cache; after a
// reconnect every query is fetched again, because missed events are not replayed.

let connection: HubConnection | null = null;
const consoleWatchers = new Map<string, number>();
let coreSeen: CoreSeen | null = null;

/** Never give up: back off to 30 s, the panel shows the link as lost meanwhile. */
const retry = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }: { previousRetryCount: number }) =>
    Math.min(30_000, 1_000 * 2 ** previousRetryCount),
};

function connect(queryClient: QueryClient) {
  const hub = new HubConnectionBuilder()
    .withUrl('/hubs/events')
    .withAutomaticReconnect(retry)
    .build();
  hub.on('InstanceChanged', (instance: InstanceSnapshot) => {
    notify(noticeForInstance(i18n.t, knownInstance(queryClient, instance.id), instance));
    applyInstanceChanged(queryClient, instance);
  });
  hub.on('InstanceRemoved', (instanceId: string) => applyInstanceRemoved(queryClient, instanceId));
  hub.on('JobUpdated', (job: JobInfo) => {
    const server =
      (job.instanceId && knownInstance(queryClient, job.instanceId)?.displayName) ?? '';
    notify(noticeForJob(i18n.t, knownJob(queryClient, job.id), job, server));
    applyJobUpdated(queryClient, job);
  });
  hub.on('ActivityAdded', (item: ActivityItem) => applyActivityAdded(queryClient, item));
  hub.on('ConsoleLines', (instanceId: string, lines: OutputLine[]) =>
    applyConsoleLines(queryClient, instanceId, lines)
  );
  hub.on('MetricsSampled', (machine: MetricPoint, instances: Record<string, MetricPoint>) =>
    applyMetricsSampled(queryClient, machine, instances)
  );
  hub.on('CoreStatus', (version: string, uptimeSec: number) => {
    setRealtimeStatus('connected');
    notify(noticeForCore(i18n.t, coreSeen, { version, uptimeSec }));
    coreSeen = { version, uptimeSec };
  });
  hub.onreconnecting(() => setRealtimeStatus('reconnecting'));
  hub.onreconnected(() => {
    setRealtimeStatus('connected');
    resubscribe(hub);
    void queryClient.invalidateQueries();
  });
  hub.onclose(() => setRealtimeStatus('disconnected'));
  return hub;
}

/** What the panel knew before an event: the server in the list or on its own page. */
function knownInstance(queryClient: QueryClient, id: string) {
  return (
    queryClient
      .getQueryData<InstanceSnapshot[]>(queries.instances().queryKey)
      ?.find((s) => s.id === id) ??
    queryClient.getQueryData<InstanceSnapshot>(queries.instance(id).queryKey)
  );
}

function knownJob(queryClient: QueryClient, id: string) {
  return (
    queryClient.getQueryData<JobInfo>(queries.job(id).queryKey) ??
    queryClient
      .getQueriesData<JobInfo[]>({ queryKey: [{ _id: 'listJobs' }] })
      .flatMap(([, list]) => list ?? [])
      .find((job) => job.id === id)
  );
}

function notify(notice: Notice | null) {
  if (notice) {
    notifications.show({
      color: notice.color,
      title: notice.detail ? notice.message : undefined,
      message: notice.detail ?? notice.message,
    });
  }
}

function resubscribe(hub: HubConnection) {
  for (const instanceId of consoleWatchers.keys()) {
    void hub.invoke('SubscribeConsole', instanceId).catch(() => undefined);
  }
}

/** Starts the link for the signed-in panel; the first connection is retried until it works or the panel stops it. */
function startRealtime(queryClient: QueryClient) {
  const hub = connect(queryClient);
  connection = hub;
  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const start = (attempt: number) => {
    setRealtimeStatus(attempt === 0 ? 'connecting' : 'disconnected');
    hub
      .start()
      .then(() => {
        setRealtimeStatus('connected');
        resubscribe(hub);
      })
      .catch(() => {
        if (!stopped) {
          timer = setTimeout(
            () => start(attempt + 1),
            retry.nextRetryDelayInMilliseconds({ previousRetryCount: attempt })
          );
        }
      });
  };
  start(0);
  return () => {
    stopped = true;
    clearTimeout(timer);
    connection = null;
    setRealtimeStatus('off');
    void hub.stop();
  };
}

/** In the application shell: on while signed in. The mock API has no hub, so the panel polls there. */
export function useRealtime() {
  const queryClient = useQueryClient();
  useEffect(
    () => (import.meta.env.MODE === 'mock' ? undefined : startRealtime(queryClient)),
    [queryClient]
  );
}

/** The console tab asks for its server's lines while it is open. */
export function useConsoleSubscription(instanceId: string) {
  useEffect(() => {
    consoleWatchers.set(instanceId, (consoleWatchers.get(instanceId) ?? 0) + 1);
    if (connection?.state === HubConnectionState.Connected) {
      void connection.invoke('SubscribeConsole', instanceId).catch(() => undefined);
    }

    return () => {
      const left = (consoleWatchers.get(instanceId) ?? 1) - 1;
      if (left > 0) {
        consoleWatchers.set(instanceId, left);
        return;
      }

      consoleWatchers.delete(instanceId);
      if (connection?.state === HubConnectionState.Connected) {
        void connection.invoke('UnsubscribeConsole', instanceId).catch(() => undefined);
      }
    };
  }, [instanceId]);
}
