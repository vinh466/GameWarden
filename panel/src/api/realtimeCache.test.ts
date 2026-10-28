import { QueryClient } from '@tanstack/react-query';
import { initialActivity, initialInstances, initialJobs } from '@/mocks/data';
import type { ActivityItem, InstanceSnapshot, JobInfo } from './generated';
import { queries } from './queries';
import {
  addMetricsSample,
  appendConsole,
  applyActivityAdded,
  applyConsoleLines,
  applyInstanceChanged,
  applyInstanceRemoved,
  applyJobUpdated,
  CONSOLE_LINES,
  METRIC_POINTS,
} from './realtimeCache';

describe('realtime events in the query cache', () => {
  it('updates a server in the list and on its own', () => {
    const client = new QueryClient();
    client.setQueryData(queries.instances().queryKey, initialInstances());
    const stopped = { ...initialInstances()[0]!, state: 'stopped' } as InstanceSnapshot;

    applyInstanceChanged(client, stopped);

    const list = client.getQueryData<InstanceSnapshot[]>(queries.instances().queryKey)!;
    expect(list.find((server) => server.id === stopped.id)?.state).toBe('stopped');
    expect(list).toHaveLength(initialInstances().length);
    expect(client.getQueryData(queries.instance(stopped.id).queryKey)).toEqual(stopped);
  });

  it('adds a new server in order and drops a deleted one', () => {
    const client = new QueryClient();
    client.setQueryData(queries.instances().queryKey, initialInstances());

    applyInstanceChanged(client, { ...initialInstances()[0]!, id: 'aaa-first' });
    applyInstanceRemoved(client, 'valheim-test');

    const ids = client
      .getQueryData<InstanceSnapshot[]>(queries.instances().queryKey)!
      .map((s) => s.id);
    expect(ids[0]).toBe('aaa-first');
    expect(ids).not.toContain('valheim-test');
  });

  it('puts a job only into the lists it belongs to', () => {
    const client = new QueryClient();
    client.setQueryData(queries.jobs().queryKey, initialJobs());
    client.setQueryData(queries.jobs('valheim-main').queryKey, []);
    client.setQueryData(queries.jobs('valheim-hardcore').queryKey, []);
    const job = { ...initialJobs()[0]!, id: 'new-job', instanceId: 'valheim-main' } as JobInfo;

    applyJobUpdated(client, job);

    expect(client.getQueryData<JobInfo[]>(queries.jobs().queryKey)![0]!.id).toBe('new-job');
    expect(client.getQueryData<JobInfo[]>(queries.jobs('valheim-main').queryKey)).toHaveLength(1);
    expect(client.getQueryData<JobInfo[]>(queries.jobs('valheim-hardcore').queryKey)).toHaveLength(
      0
    );
    expect(client.getQueryData(queries.job('new-job').queryKey)).toEqual(job);
  });

  it('puts new activity on top of the matching feeds', () => {
    const client = new QueryClient();
    client.setQueryData(queries.activity().queryKey, initialActivity());
    client.setQueryData(queries.activity('valheim-hardcore').queryKey, []);
    const item: ActivityItem = { ...initialActivity()[0]!, id: 100, instanceId: 'valheim-main' };

    applyActivityAdded(client, item);

    expect(client.getQueryData<ActivityItem[]>(queries.activity().queryKey)![0]!.id).toBe(100);
    expect(
      client.getQueryData<ActivityItem[]>(queries.activity('valheim-hardcore').queryKey)
    ).toHaveLength(0);
  });

  it('adds metric samples, keeps ten minutes and forgets stopped servers', () => {
    const point = (cpuPercent: number) => ({ at: '2026-10-28T10:00:00Z', cpuPercent, ramMb: 1000 });
    const report = {
      machine: { latest: point(1), history: Array.from({ length: METRIC_POINTS }, () => point(1)) },
      instances: { 'valheim-main': { latest: point(1), history: [point(1)] } },
    };

    const next = addMetricsSample(report, point(50), { 'valheim-test': point(9) });

    expect(next.machine.latest?.cpuPercent).toBe(50);
    expect(next.machine.history).toHaveLength(METRIC_POINTS);
    expect(next.machine.history.at(-1)?.cpuPercent).toBe(50);
    expect(Object.keys(next.instances)).toEqual(['valheim-test']);
    expect(next.instances['valheim-test']?.history).toHaveLength(1);
  });

  it('appends console lines and keeps the newest', () => {
    const client = new QueryClient();
    const line = (text: string) => ({
      at: '2026-10-28T10:00:00Z',
      stream: 'stdout' as const,
      text,
    });
    client.setQueryData(queries.console('valheim-main').queryKey, { lines: [line('a')] });

    applyConsoleLines(client, 'valheim-main', [line('b')]);

    expect(
      client
        .getQueryData<{ lines: { text: string }[] }>(queries.console('valheim-main').queryKey)
        ?.lines.map((l) => l.text)
    ).toEqual(['a', 'b']);
    const full = { lines: Array.from({ length: CONSOLE_LINES }, (_, i) => line(String(i))) };
    expect(appendConsole(full, [line('newest')]).lines.at(-1)?.text).toBe('newest');
    expect(appendConsole(full, [line('newest')]).lines).toHaveLength(CONSOLE_LINES);
  });
});
