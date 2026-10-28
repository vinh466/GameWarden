import type { ActivityItem } from '@/api/generated';

/** docs/11 §2 Activity: "filter by server and type". The groups people look for, from the line's kind. */
export const activityGroups = ['all', 'servers', 'jobs', 'commands', 'system'] as const;

export type ActivityGroup = (typeof activityGroups)[number];

export function activityGroup(item: ActivityItem): Exclude<ActivityGroup, 'all'> {
  if (item.kind.startsWith('instance.')) {
    return 'servers';
  }

  if (item.kind.startsWith('job.')) {
    return 'jobs';
  }

  return item.kind === 'command' ? 'commands' : 'system';
}
