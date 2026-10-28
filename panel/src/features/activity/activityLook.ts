import {
  IconAlertTriangle,
  IconCheck,
  IconCloudDownload,
  IconDownload,
  IconPlayerPlay,
  IconPlayerStop,
  IconPlus,
  IconPower,
  IconTrash,
  IconUser,
  IconX,
  type Icon,
} from '@tabler/icons-react';
import type { MantineColor } from '@mantine/core';
import { statusColors } from '@/theme';

/** How a line of the activity feed looks: by its kind, with the level deciding red / yellow (docs/11 §5). */
export function activityLook(kind: string, level: string): { color: MantineColor; icon: Icon } {
  const icon =
    {
      'instance.created': IconPlus,
      'instance.deleted': IconTrash,
      'instance.installed': IconDownload,
      'instance.running': IconPlayerPlay,
      'instance.stopped': IconPlayerStop,
      'instance.crashed': IconAlertTriangle,
      'instance.faulted': IconAlertTriangle,
      'job.succeeded': IconCheck,
      'job.failed': IconX,
      command: IconUser,
      update: IconCloudDownload,
      'core.started': IconPower,
    }[kind] ?? IconCheck;
  const color: MantineColor =
    level === 'error'
      ? statusColors.failed
      : level === 'warning'
        ? 'yellow'
        : ['instance.running', 'instance.installed', 'job.succeeded'].includes(kind)
          ? statusColors.running
          : kind === 'command'
            ? 'violet'
            : statusColors.idle;
  return { color, icon };
}
