import { IconCheck, IconClock, IconLoader2, IconX, type Icon } from '@tabler/icons-react';
import type { MantineColor } from '@mantine/core';
import type { JobState } from '@/api/generated';
import { statusColors } from '@/theme';

export const jobLook: Record<JobState, { color: MantineColor; icon: Icon }> = {
  queued: { color: statusColors.idle, icon: IconClock },
  running: { color: statusColors.busy, icon: IconLoader2 },
  succeeded: { color: statusColors.running, icon: IconCheck },
  failed: { color: statusColors.failed, icon: IconX },
};
