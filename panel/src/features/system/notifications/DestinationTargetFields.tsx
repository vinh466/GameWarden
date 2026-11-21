import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { MultiSelect } from '@mantine/core';
import type { NotificationDestinationUpdate, NotificationSettingsView } from '@/api/generated';
import { HelpField } from '@/components/help/HelpLabel';
import { DestinationOption } from './DestinationOption';

/** Which topics and servers a destination follows; `*` means all of them. */
export function DestinationTargetFields({
  value,
  set,
  catalog,
  instances,
}: {
  value: NotificationDestinationUpdate;
  set: <K extends keyof NotificationDestinationUpdate>(
    key: K,
    next: NotificationDestinationUpdate[K]
  ) => void;
  catalog: NotificationSettingsView['catalog'];
  instances: { id: string; label: string; state: string }[];
}) {
  const { t } = useTranslation();
  const topicData = useMemo(() => {
    const groups = new Map<string, { value: string; label: string }[]>();
    for (const topic of catalog) {
      const items = groups.get(topic.category) ?? [];
      items.push({ value: topic.key, label: topic.key });
      groups.set(topic.category, items);
    }

    return [
      {
        group: t('system.notifications.allTopics'),
        items: [{ value: '*', label: '*' }],
      },
      ...[...groups.entries()].map(([group, items]) => ({ group, items })),
    ];
  }, [catalog, t]);

  return (
    <>
      <HelpField
        label={t('system.notifications.topics')}
        help={{ topicId: 'notifications.topics' }}
      >
        {(id) => (
          <MultiSelect
            id={id}
            description={t('system.notifications.topicsHint')}
            data={topicData}
            searchable
            clearable
            value={value.topics ?? []}
            onChange={(next) => set('topics', next)}
            renderOption={({ option }) => {
              const topic = catalog.find((item) => item.key === option.value);
              return (
                <DestinationOption
                  label={option.label}
                  description={
                    option.value === '*'
                      ? t('system.notifications.topicAllHelp')
                      : topic
                        ? t('system.notifications.topicHelp', {
                            category: topic.category,
                            severity: topic.defaultSeverity,
                          })
                        : undefined
                  }
                />
              );
            }}
          />
        )}
      </HelpField>
      <HelpField
        label={t('system.notifications.instances')}
        help={{ topicId: 'notifications.instances' }}
      >
        {(id) => (
          <MultiSelect
            id={id}
            description={t('system.notifications.instancesHint')}
            data={[
              { value: '*', label: t('system.notifications.all') },
              ...instances.map((instance) => ({
                value: instance.id,
                label: instance.label,
              })),
            ]}
            searchable
            clearable
            value={value.instances ?? []}
            onChange={(next) => set('instances', next)}
            renderOption={({ option }) => {
              const instance = instances.find((item) => item.id === option.value);
              return (
                <DestinationOption
                  label={option.label}
                  description={
                    option.value === '*'
                      ? t('system.notifications.allInstancesHelp')
                      : instance
                        ? t('system.notifications.instanceHelp', {
                            id: instance.id,
                            state: instance.state,
                          })
                        : undefined
                  }
                />
              );
            }}
          />
        )}
      </HelpField>
    </>
  );
}
