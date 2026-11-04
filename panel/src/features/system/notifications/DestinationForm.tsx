import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Divider,
  Group,
  MultiSelect,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
} from '@mantine/core';
import type { NotificationDestinationUpdate, NotificationSettingsView } from '@/api/generated';
import { HelpField } from '@/components/help/HelpLabel';
import { notificationSeverities } from './notifications';

/** Add or edit a destination; the webhook is write-only and never prefilled (docs/18). */
export function DestinationForm({
  destination,
  modes,
  catalog,
  instances,
  editing,
  busy,
  onSave,
  onCancel,
}: {
  destination: NotificationDestinationUpdate;
  modes: string[];
  catalog: NotificationSettingsView['catalog'];
  instances: { id: string; label: string; state: string }[];
  editing: boolean;
  busy: boolean;
  onSave: (destination: NotificationDestinationUpdate) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const [value, setValue] = useState(destination);
  useEffect(() => setValue(destination), [destination]);
  const topicData = useMemo(() => {
    const groups = new Map<string, { value: string; label: string }[]>();
    for (const topic of catalog) {
      const items = groups.get(topic.category) ?? [];
      items.push({ value: topic.key, label: topic.key });
      groups.set(topic.category, items);
    }

    return [
      { group: t('system.notifications.allTopics'), items: [{ value: '*', label: '*' }] },
      ...[...groups.entries()].map(([group, items]) => ({ group, items })),
    ];
  }, [catalog, t]);
  const modeLabels: Record<string, string> = {
    instant: t('system.notifications.modes.instant'),
    digest: t('system.notifications.modes.digest'),
    status: t('system.notifications.modes.status'),
  };
  const modeHelp: Record<string, string> = {
    instant: t('system.notifications.modeHelp.instant'),
    digest: t('system.notifications.modeHelp.digest'),
    status: t('system.notifications.modeHelp.status'),
  };
  const modeData = modes.map((mode) => ({
    value: mode,
    label: modeLabels[mode] ?? mode,
  }));
  const languageHelp: Record<string, string> = {
    vi: t('system.notifications.languageHelp.vi'),
    en: t('system.notifications.languageHelp.en'),
  };
  const languageData = [
    { value: 'vi', label: 'Tiếng Việt' },
    { value: 'en', label: 'English' },
  ];
  const severityLabels: Record<string, string> = {
    info: t('system.notifications.severities.info'),
    success: t('system.notifications.severities.success'),
    warning: t('system.notifications.severities.warning'),
    error: t('system.notifications.severities.error'),
    critical: t('system.notifications.severities.critical'),
  };
  const severityHelp: Record<string, string> = {
    info: t('system.notifications.severityHelp.info'),
    success: t('system.notifications.severityHelp.success'),
    warning: t('system.notifications.severityHelp.warning'),
    error: t('system.notifications.severityHelp.error'),
    critical: t('system.notifications.severityHelp.critical'),
  };
  const severityData = notificationSeverities.map((severity) => ({
    value: severity,
    label: severityLabels[severity] ?? severity,
  }));

  const set = <K extends keyof NotificationDestinationUpdate>(
    key: K,
    next: NotificationDestinationUpdate[K]
  ) => setValue((current) => ({ ...current, [key]: next }));

  return (
    <Stack gap="sm">
      <Group grow>
        <HelpField
          label={t('system.notifications.id')}
          help={{ topicId: 'notifications.identity' }}
          required
        >
          {(id) => (
            <TextInput
              id={id}
              description={t('system.notifications.idHint')}
              disabled={editing}
              value={value.id}
              onChange={(event) => set('id', event.currentTarget.value)}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.name')}
          help={{ topicId: 'notifications.identity' }}
          required
        >
          {(id) => (
            <TextInput
              id={id}
              value={value.name}
              onChange={(event) => set('name', event.currentTarget.value)}
            />
          )}
        </HelpField>
      </Group>
      <HelpField
        label={t('system.notifications.webhook')}
        help={{ topicId: 'notifications.webhook' }}
        required={!editing}
      >
        {(id) => (
          <PasswordInput
            id={id}
            description={t('system.notifications.webhookHint')}
            placeholder={
              editing
                ? t('system.notifications.webhookKeep')
                : 'https://discord.com/api/webhooks/...'
            }
            value={value.webhookUrl ?? ''}
            onChange={(event) => set('webhookUrl', event.currentTarget.value || null)}
          />
        )}
      </HelpField>
      <Group grow>
        <HelpField label={t('system.notifications.mode')} help={{ topicId: 'notifications.mode' }}>
          {(id) => (
            <Select
              id={id}
              data={modeData}
              value={value.mode}
              onChange={(next) => set('mode', next ?? 'instant')}
              allowDeselect={false}
              renderOption={({ option }) => (
                <Option label={option.label} description={modeHelp[option.value]} />
              )}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.language')}
          help={{ topicId: 'notifications.language' }}
        >
          {(id) => (
            <Select
              id={id}
              data={languageData}
              value={value.language}
              onChange={(next) => set('language', next ?? 'vi')}
              allowDeselect={false}
              renderOption={({ option }) => (
                <Option label={option.label} description={languageHelp[option.value]} />
              )}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.minimumSeverity')}
          help={{ topicId: 'notifications.minimumSeverity' }}
        >
          {(id) => (
            <Select
              id={id}
              data={severityData}
              value={value.minimumSeverity}
              onChange={(next) => set('minimumSeverity', next ?? 'warning')}
              allowDeselect={false}
              renderOption={({ option }) => (
                <Option label={option.label} description={severityHelp[option.value]} />
              )}
            />
          )}
        </HelpField>
      </Group>
      {value.mode === 'digest' && (
        <Text size="xs" c="dimmed">
          {t('system.notifications.modeDigestHint')}
        </Text>
      )}
      {value.mode === 'status' && (
        <Text size="xs" c="dimmed">
          {t('system.notifications.modeStatusHint')}
        </Text>
      )}
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
                <Option
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
              ...instances.map((instance) => ({ value: instance.id, label: instance.label })),
            ]}
            searchable
            clearable
            value={value.instances ?? []}
            onChange={(next) => set('instances', next)}
            renderOption={({ option }) => {
              const instance = instances.find((item) => item.id === option.value);
              return (
                <Option
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
      <Group grow>
        <HelpField
          label={t('system.notifications.mentionRole')}
          help={{ topicId: 'notifications.mentionRole' }}
        >
          {(id) => (
            <TextInput
              id={id}
              description={t('system.notifications.mentionRoleHint')}
              value={value.mentionRoleId ?? ''}
              onChange={(event) => set('mentionRoleId', event.currentTarget.value || null)}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.panelUrl')}
          help={{ topicId: 'notifications.panelUrl' }}
        >
          {(id) => (
            <TextInput
              id={id}
              placeholder="http://100.96.2.89:8765"
              value={value.panelUrl ?? ''}
              onChange={(event) => set('panelUrl', event.currentTarget.value || null)}
            />
          )}
        </HelpField>
      </Group>
      <Group grow>
        <HelpField
          label={t('system.notifications.intervalSec')}
          help={{ topicId: 'notifications.timing' }}
        >
          {(id) => (
            <NumberInput
              id={id}
              min={10}
              max={3600}
              value={value.intervalSec}
              onChange={(next) => set('intervalSec', Number(next) || 60)}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.digestSec')}
          help={{ topicId: 'notifications.timing' }}
        >
          {(id) => (
            <NumberInput
              id={id}
              min={30}
              max={3600}
              value={value.digestSec}
              onChange={(next) => set('digestSec', Number(next) || 300)}
            />
          )}
        </HelpField>
      </Group>
      <HelpField
        label={t('system.notifications.enabled')}
        help={{ topicId: 'notifications.enabled' }}
      >
        {(id) => (
          <Switch
            id={id}
            checked={value.enabled}
            onChange={(event) => set('enabled', event.currentTarget.checked)}
          />
        )}
      </HelpField>
      <Divider />
      <Group justify="flex-end">
        <Button variant="default" onClick={onCancel}>
          {t('actions.cancel')}
        </Button>
        <Button
          loading={busy}
          disabled={!value.id || !value.name || (!editing && !value.webhookUrl)}
          onClick={() => onSave(value)}
        >
          {t('actions.save')}
        </Button>
      </Group>
    </Stack>
  );
}

function Option({ label, description }: { label: string; description?: string }) {
  return (
    <Stack gap={0}>
      <Text size="sm">{label}</Text>
      {description && (
        <Text size="xs" c="dimmed">
          {description}
        </Text>
      )}
    </Stack>
  );
}
