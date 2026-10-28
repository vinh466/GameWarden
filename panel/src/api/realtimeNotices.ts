import type { TFunction } from 'i18next';
import { jobTypeLabel } from '@/components/JobProgress';
import type { InstanceSnapshot, JobInfo } from './generated';

// Toasts for what happens while the panel is open (docs/11 §2 "Everywhere"): a job finishing, a server crashing,
// Core restarting or changing version. Each compares the event with what the panel knew just before, so only a real
// transition shows, once.

export interface Notice {
  color: 'green' | 'red' | 'violet' | 'orange';
  message: string;
  detail?: string | null;
}

export function noticeForJob(
  t: TFunction,
  before: JobInfo | undefined,
  job: JobInfo,
  server: string
): Notice | null {
  if (!job.finishedAt || before?.finishedAt) {
    return null;
  }

  const type = jobTypeLabel(t, job.type);
  return job.state === 'succeeded'
    ? { color: 'green', message: t('feedback.jobSucceeded', { type, server }) }
    : { color: 'red', message: t('feedback.jobFailed', { type, server }), detail: job.message };
}

export function noticeForInstance(
  t: TFunction,
  before: InstanceSnapshot | undefined,
  after: InstanceSnapshot
): Notice | null {
  if (!before || before.state === after.state) {
    return null;
  }

  if (after.state === 'crashed') {
    return {
      color: 'red',
      message: t('feedback.crashed', { server: after.displayName }),
      detail: after.lastProblem,
    };
  }

  return after.state === 'faulted'
    ? {
        color: 'red',
        message: t('feedback.faulted', { server: after.displayName }),
        detail: after.lastProblem,
      }
    : null;
}

export interface CoreSeen {
  version: string;
  uptimeSec: number;
}

/** From the heartbeat: a new version means an update, a smaller uptime a restart (docs/07 §8). */
export function noticeForCore(t: TFunction, before: CoreSeen | null, now: CoreSeen): Notice | null {
  if (!before) {
    return null;
  }

  if (before.version !== now.version) {
    return { color: 'violet', message: t('feedback.coreUpdated', { version: now.version }) };
  }

  return now.uptimeSec < before.uptimeSec
    ? { color: 'orange', message: t('feedback.coreRestarted') }
    : null;
}
