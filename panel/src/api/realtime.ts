import { useEffect } from 'react';
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr';
import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import type { ActivityItem, InstanceSnapshot, JobInfo, OutputLine } from './generated';
import {
  applyActivityAdded,
  applyConsoleLines,
  applyInstanceChanged,
  applyInstanceRemoved,
  applyJobUpdated,
} from './realtimeCache';
import { setRealtimeStatus } from './realtimeStatus';

// The realtime link (docs/05 §3, Microsoft's SignalR JavaScript client): events update the query cache; after a
// reconnect every query is fetched again, because missed events are not replayed.

let connection: HubConnection | null = null;
const consoleWatchers = new Map<string, number>();

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
  hub.on('InstanceChanged', (instance: InstanceSnapshot) =>
    applyInstanceChanged(queryClient, instance)
  );
  hub.on('InstanceRemoved', (instanceId: string) => applyInstanceRemoved(queryClient, instanceId));
  hub.on('JobUpdated', (job: JobInfo) => applyJobUpdated(queryClient, job));
  hub.on('ActivityAdded', (item: ActivityItem) => applyActivityAdded(queryClient, item));
  hub.on('ConsoleLines', (instanceId: string, lines: OutputLine[]) =>
    applyConsoleLines(queryClient, instanceId, lines)
  );
  hub.on('CoreStatus', () => setRealtimeStatus('connected'));
  hub.onreconnecting(() => setRealtimeStatus('reconnecting'));
  hub.onreconnected(() => {
    setRealtimeStatus('connected');
    resubscribe(hub);
    void queryClient.invalidateQueries();
  });
  hub.onclose(() => setRealtimeStatus('disconnected'));
  return hub;
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
