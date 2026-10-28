import dayjs from 'dayjs';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text, ThemeIcon, Timeline } from '@mantine/core';
import type { ActivityItem } from '@/api/generated';
import { queries } from '@/api/queries';
import { AnchorLink } from '@/components/links';
import { activityLook } from './activityLook';

/** The activity feed as a timeline (docs/11 §2): what happened, to which server, who asked, when. */
export function ActivityFeed({
  items,
  compact = false,
}: {
  items: ActivityItem[];
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const instances = useQuery(queries.instances());
  const name = (id: string) =>
    instances.data?.find((server) => server.id === id)?.displayName ?? id;

  if (items.length === 0) {
    return (
      <Text c="dimmed" size="sm">
        {t('activity.empty')}
      </Text>
    );
  }

  return (
    <Timeline bulletSize={compact ? 20 : 24} lineWidth={2} mt={compact ? 'xs' : undefined}>
      {items.map((item) => {
        const look = activityLook(item.kind, item.level);
        const Icon = look.icon;
        return (
          // Timeline colours only "active" items, so the bullet carries the colour itself (Mantine's custom bullets).
          <Timeline.Item
            key={item.id}
            bullet={
              <ThemeIcon size={compact ? 20 : 24} radius="xl" variant="light" color={look.color}>
                <Icon size={compact ? 12 : 14} />
              </ThemeIcon>
            }
            title={
              <Text
                size="sm"
                fw={500}
                c={item.level === 'error' ? 'red' : undefined}
                lineClamp={compact ? 1 : 2}
              >
                {item.message}
              </Text>
            }
          >
            <Text size="xs" c="dimmed" title={dayjs(item.at).format('LLL')} lineClamp={1}>
              {item.instanceId ? (
                <AnchorLink to="/servers/$id" params={{ id: item.instanceId }} inherit c="dimmed">
                  {name(item.instanceId)}
                </AnchorLink>
              ) : (
                t('activity.system')
              )}
              {item.user ? ` · ${t('activity.by', { user: item.user })}` : ''} ·{' '}
              {dayjs(item.at).fromNow()}
            </Text>
          </Timeline.Item>
        );
      })}
    </Timeline>
  );
}
