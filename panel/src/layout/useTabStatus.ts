import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useMantineTheme } from '@mantine/core';
import type { InstanceSnapshot, JobInfo } from '@/api/generated';
import { queries } from '@/api/queries';
import { useRealtimeStatus, type RealtimeStatus } from '@/api/realtimeStatus';
import { useAttention } from '@/features/dashboard/useAttention';

export type TabStatus = 'red' | 'yellow' | 'green' | 'gray';

/**
 * docs/07 §8 "Is it running?": the tab shows the overall state without opening it. Red when something needs
 * attention or the link to Core is lost, yellow while something is in progress, green when servers run, grey when
 * none does.
 */
export function tabStatus(
  attention: number,
  link: RealtimeStatus,
  servers: InstanceSnapshot[],
  jobs: JobInfo[]
): TabStatus {
  if (attention > 0 || link === 'disconnected') {
    return 'red';
  }

  const busy =
    jobs.some((job) => job.state === 'running' || job.state === 'queued') ||
    servers.some((server) => server.state === 'starting' || server.state === 'stopping');
  if (busy) {
    return 'yellow';
  }

  return servers.some((server) => server.state === 'running') ? 'green' : 'gray';
}

/** The shield of public/favicon.svg in the status colour. */
function favicon(fill: string) {
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">` +
    `<path d="M16 2 4 6.5v8.8c0 7.3 5.1 13.6 12 15.7 6.9-2.1 12-8.4 12-15.7V6.5L16 2Z" fill="${fill}"/>` +
    `<path d="M11 16.5 14.5 20 21.5 12" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

/** In the signed-in shell: sets the favicon and the `(1⚠) GameWarden` title; puts both back when it closes. */
export function useTabStatus() {
  const theme = useMantineTheme();
  const attention = useAttention();
  const link = useRealtimeStatus();
  const instances = useQuery(queries.instances());
  const jobs = useQuery(queries.jobs());
  const count = attention?.length ?? 0;
  const status = tabStatus(count, link, instances.data ?? [], jobs.data ?? []);

  useEffect(() => {
    const icon = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    const original = icon?.href;
    if (icon) {
      icon.href = favicon(theme.colors[status][6]);
    }

    document.title = count > 0 ? `(${count}⚠) GameWarden` : 'GameWarden';
    return () => {
      if (icon && original) {
        icon.href = original;
      }

      document.title = 'GameWarden';
    };
  }, [status, count, theme]);
}
