import {
  IconAlertTriangle,
  IconCircleCheckFilled,
  IconCircleX,
  IconDownload,
  IconLoader2,
  IconPlayerStopFilled,
  type Icon,
} from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, type BadgeProps, type MantineColor } from '@mantine/core';
import type { ActualState } from '@/api/generated';
import { statusColors } from '@/theme';

/** The one place that decides how an instance state looks (docs/11 §5): colour, icon and word together. */
export const stateLook: Record<
  ActualState,
  { color: MantineColor; icon: Icon; spinning?: boolean }
> = {
  running: { color: statusColors.running, icon: IconCircleCheckFilled },
  starting: { color: statusColors.transitional, icon: IconLoader2, spinning: true },
  stopping: { color: statusColors.transitional, icon: IconLoader2, spinning: true },
  crashed: { color: statusColors.failed, icon: IconAlertTriangle },
  faulted: { color: statusColors.failed, icon: IconCircleX },
  stopped: { color: statusColors.idle, icon: IconPlayerStopFilled },
  notInstalled: { color: statusColors.idle, icon: IconDownload },
};

export function StatusBadge({
  state,
  ...props
}: { state: ActualState } & Omit<BadgeProps, 'color' | 'leftSection'>) {
  const { t } = useTranslation();
  const look = stateLook[state];
  const Icon = look.icon;
  return (
    <Badge
      color={look.color}
      leftSection={<Icon size={12} className={look.spinning ? 'gw-spin' : undefined} aria-hidden />}
      {...props}
    >
      {t(`state.${state}`)}
    </Badge>
  );
}

export function BusyBadge({ label }: { label: string }) {
  const { t } = useTranslation();
  return (
    <Badge
      color={statusColors.busy}
      leftSection={<IconLoader2 size={12} className="gw-spin" aria-hidden />}
      title={label}
    >
      {t('state.busy')}
    </Badge>
  );
}
