import { IconFilter, IconRefresh, IconSearch } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Drawer, Group, Select, Stack, Switch, TextInput } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';

export interface LogFilterState {
  level: string | null;
  component: string | null;
  instanceId: string | null;
  jobId: string;
  requestId: string;
  window: string;
  from?: string;
  search: string;
  limit: string;
  paused: boolean;
}

/** Every filter of the System log in one toolbar; on phones the fields move into a drawer (docs/19). */
export function LogFilters({
  value,
  onChange,
  onClear,
  components,
  instances,
  refreshing,
  onRefresh,
}: {
  value: LogFilterState;
  onChange: (next: Partial<LogFilterState>) => void;
  onClear: () => void;
  components: Array<{ component: string }>;
  instances: Array<{ id: string; displayName: string }>;
  refreshing: boolean;
  onRefresh: () => void;
}) {
  const { t } = useTranslation();
  const [opened, drawer] = useDisclosure(false);
  const activeCount = [
    value.level,
    value.component,
    value.instanceId,
    value.jobId,
    value.requestId,
    value.from,
    value.search,
  ].filter(Boolean).length;

  return (
    <>
      <Group justify="space-between" gap="sm" hiddenFrom="sm" wrap="nowrap">
        <Button
          variant="default"
          leftSection={<IconFilter size={16} />}
          onClick={drawer.open}
          rightSection={activeCount > 0 ? <Badge size="xs">{activeCount}</Badge> : undefined}
        >
          {t('system.logs.filters')}
        </Button>
        <Group gap="xs" wrap="nowrap">
          <Switch
            label={t('system.logs.live')}
            checked={!value.paused}
            onChange={(event) => onChange({ paused: !event.currentTarget.checked })}
          />
          <Button
            variant="light"
            leftSection={<IconRefresh size={16} />}
            onClick={onRefresh}
            loading={refreshing}
          >
            {t('actions.refresh')}
          </Button>
        </Group>
      </Group>

      <Group align="flex-end" gap="sm" wrap="wrap" visibleFrom="sm">
        <FilterFields
          value={value}
          onChange={onChange}
          components={components}
          instances={instances}
          layout="bare"
        />
        <Switch
          label={t('system.logs.live')}
          checked={!value.paused}
          onChange={(event) => onChange({ paused: !event.currentTarget.checked })}
        />
        <Button
          variant="light"
          leftSection={<IconRefresh size={16} />}
          onClick={onRefresh}
          loading={refreshing}
        >
          {t('actions.refresh')}
        </Button>
      </Group>

      <Drawer
        opened={opened}
        onClose={drawer.close}
        position="bottom"
        size="85%"
        title={t('system.logs.filters')}
      >
        <Stack gap="sm">
          <FilterFields
            value={value}
            onChange={onChange}
            components={components}
            instances={instances}
            layout="stack"
          />
          <Group justify="space-between" mt="sm">
            <Button variant="default" onClick={onClear} disabled={activeCount === 0}>
              {t('system.logs.clearFilters')}
            </Button>
            <Button onClick={drawer.close}>{t('actions.close')}</Button>
          </Group>
        </Stack>
      </Drawer>
    </>
  );
}

function FilterFields({
  value,
  onChange,
  components,
  instances,
  layout,
}: {
  value: LogFilterState;
  onChange: (next: Partial<LogFilterState>) => void;
  components: Array<{ component: string }>;
  instances: Array<{ id: string; displayName: string }>;
  layout: 'row' | 'stack' | 'bare';
}) {
  const { t } = useTranslation();
  const fields = [
    <Select
      key="level"
      label={t('system.logs.level')}
      placeholder={t('system.logs.allLevels')}
      clearable
      w={layout !== 'stack' ? 150 : undefined}
      value={value.level}
      onChange={(level) => onChange({ level })}
      data={(['trace', 'debug', 'info', 'warn', 'error', 'critical'] as const).map((level) => ({
        value: level,
        label: t(`system.logs.levels.${level}`),
      }))}
    />,
    <Select
      key="component"
      label={t('system.logs.component')}
      placeholder={t('system.logs.allComponents')}
      clearable
      searchable
      w={layout !== 'stack' ? 260 : undefined}
      value={value.component}
      onChange={(component) => onChange({ component })}
      data={components.map((item) => ({ value: item.component, label: item.component }))}
    />,
    <Select
      key="instance"
      label={t('system.logs.instance')}
      placeholder={t('system.logs.allInstances')}
      clearable
      w={layout !== 'stack' ? 190 : undefined}
      value={value.instanceId}
      onChange={(instanceId) => onChange({ instanceId })}
      data={instances.map((instance) => ({
        value: instance.id,
        label: instance.displayName,
      }))}
    />,
    <TextInput
      key="job"
      label={t('system.logs.job')}
      placeholder={t('system.logs.jobPlaceholder')}
      w={layout !== 'stack' ? 190 : undefined}
      value={value.jobId}
      onChange={(event) => onChange({ jobId: event.currentTarget.value })}
    />,
    <TextInput
      key="request"
      label={t('system.logs.request')}
      placeholder={t('system.logs.requestPlaceholder')}
      w={layout !== 'stack' ? 190 : undefined}
      value={value.requestId}
      onChange={(event) => onChange({ requestId: event.currentTarget.value })}
    />,
    <TextInput
      key="search"
      label={t('system.logs.search')}
      placeholder={t('system.logs.searchPlaceholder')}
      leftSection={<IconSearch size={16} />}
      w={layout !== 'stack' ? 240 : undefined}
      value={value.search}
      onChange={(event) => onChange({ search: event.currentTarget.value })}
    />,
    <Select
      key="window"
      label={t('system.logs.window')}
      w={layout !== 'stack' ? 150 : undefined}
      value={value.window}
      onChange={(window) => {
        const next = window ?? 'all';
        onChange({
          window: next,
          from:
            next === 'all' ? undefined : new Date(Date.now() - Number(next) * 60_000).toISOString(),
        });
      }}
      data={[
        { value: 'all', label: t('system.logs.allTime') },
        { value: '15', label: t('system.logs.last15') },
        { value: '60', label: t('system.logs.last60') },
        { value: '1440', label: t('system.logs.last24') },
        { value: '10080', label: t('system.logs.last7') },
      ]}
    />,
    <Select
      key="limit"
      label={t('system.logs.rows')}
      w={layout !== 'stack' ? 110 : undefined}
      value={value.limit}
      onChange={(limit) => onChange({ limit: limit ?? '100' })}
      data={['100', '250', '500']}
    />,
  ];

  if (layout === 'bare') {
    return <>{fields}</>;
  }

  return layout === 'row' ? (
    <Group align="flex-end" gap="sm" wrap="wrap">
      {fields}
    </Group>
  ) : (
    <Stack gap="sm">{fields}</Stack>
  );
}
