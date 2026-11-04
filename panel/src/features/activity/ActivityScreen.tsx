import { IconActivity } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Card, Group, SegmentedControl, Select, Skeleton } from '@mantine/core';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';
import { ActivityFeed } from '@/features/activity/ActivityFeed';
import {
  activityGroup,
  activityGroups,
  type ActivityGroup,
} from '@/features/activity/activityKinds';
import { SystemTab } from '@/features/system/SystemTab';

/** The feed of docs/11 §2: what happened, to which server, who asked and when. Filters live in the URL. */
export function ActivityScreen({
  server,
  type,
  onServerChange,
  onTypeChange,
}: {
  server?: string;
  type: ActivityGroup;
  onServerChange: (server?: string) => void;
  onTypeChange: (type: ActivityGroup) => void;
}) {
  const { t } = useTranslation();
  const instances = useQuery(queries.instances());
  const activity = useQuery(queries.activity(server));
  const items = activity.data?.filter((item) => type === 'all' || activityGroup(item) === type);

  return (
    <SystemTab
      description={t('activity.description')}
      actions={
        <Group gap="xs" wrap="wrap">
          <SegmentedControl
            size="xs"
            value={type}
            onChange={(value) => onTypeChange(value as ActivityGroup)}
            data={activityGroups.map((group) => ({
              value: group,
              label: t(`activity.types.${group}`),
            }))}
            aria-label={t('activity.type')}
          />
          <Select
            placeholder={t('activity.allServers')}
            clearable
            w={200}
            value={server ?? null}
            onChange={(value) => onServerChange(value ?? undefined)}
            data={(instances.data ?? []).map((instance) => ({
              value: instance.id,
              label: instance.displayName,
            }))}
            aria-label={t('jobs.server')}
          />
        </Group>
      }
    >
      <Card maw={900}>
        {activity.isError ? (
          <ProblemAlert error={activity.error} onRetry={() => void activity.refetch()} />
        ) : !items ? (
          <Skeleton h={240} />
        ) : items.length === 0 ? (
          <EmptyState
            icon={IconActivity}
            title={t('activity.empty')}
            text={type === 'all' ? undefined : t('activity.emptyFiltered')}
          />
        ) : (
          <ActivityFeed items={items} />
        )}
      </Card>
    </SystemTab>
  );
}
