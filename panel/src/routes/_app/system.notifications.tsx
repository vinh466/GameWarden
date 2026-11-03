import { useEffect, useMemo, useState } from 'react';
import { IconAlertTriangle, IconEdit, IconPlus, IconSend, IconTrash } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Badge,
  Button,
  Card,
  Divider,
  Group,
  Modal,
  MultiSelect,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import {
  retryNotificationDelivery,
  testNotificationDestination,
  updateNotificationSettings,
  type NotificationDeliveryView,
  type NotificationDestinationUpdate,
  type NotificationDestinationView,
  type NotificationSettingsView,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';

export const Route = createFileRoute('/_app/system/notifications')({
  component: Notifications,
});

const severities = ['info', 'success', 'warning', 'error', 'critical'];

function Notifications() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const settings = useQuery(queries.notificationSettings());
  const deliveries = useQuery(queries.notificationDeliveries());
  const instances = useQuery(queries.instances());
  const [editing, setEditing] = useState<NotificationDestinationUpdate | null>(null);
  const [modalOpen, modal] = useDisclosure(false);

  const save = useMutation({
    mutationFn: async (destinations: NotificationDestinationUpdate[]) =>
      (await updateNotificationSettings({ body: { destinations }, throwOnError: true })).data,
    onSuccess: async (saved) => {
      queryClient.setQueryData(queries.notificationSettings().queryKey, saved);
      await queryClient.invalidateQueries({ queryKey: queries.notificationSettings().queryKey });
      notifications.show({ color: 'green', message: t('system.notifications.saved') });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const test = useMutation({
    mutationFn: (id: string) => testNotificationDestination({ path: { id }, throwOnError: true }),
    onSuccess: () =>
      notifications.show({ color: 'green', message: t('system.notifications.testSent') }),
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('system.notifications.testFailed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const retry = useMutation({
    mutationFn: (id: number) => retryNotificationDelivery({ path: { id }, throwOnError: true }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queries.notificationDeliveries().queryKey });
      notifications.show({ color: 'green', message: t('system.notifications.retried') });
    },
  });

  if (settings.isError) {
    return <ProblemAlert error={settings.error} onRetry={() => void settings.refetch()} />;
  }

  if (!settings.data) {
    return <Text c="dimmed">{t('system.notifications.loading')}</Text>;
  }

  const data = settings.data;
  const openNew = () => {
    setEditing(blankDestination(data.supportedModes[0] ?? 'instant'));
    modal.open();
  };
  const openEdit = (destination: NotificationDestinationView) => {
    setEditing(toUpdate(destination));
    modal.open();
  };
  const saveOne = (destination: NotificationDestinationUpdate) => {
    const current = data.destinations.map(toUpdate);
    const next = current.some((item) => item.id === destination.id)
      ? current.map((item) => (item.id === destination.id ? destination : item))
      : [...current, destination];
    save.mutate(next);
    modal.close();
  };
  const remove = (id: string) => {
    save.mutate(data.destinations.filter((destination) => destination.id !== id).map(toUpdate));
  };

  return (
    <Stack gap="lg">
      <Text c="dimmed" size="sm">
        {t('system.notifications.description')}
      </Text>
      <Alert color="blue" icon={<IconAlertTriangle />} title={t('system.notifications.pinTitle')}>
        {t('system.notifications.pinText')}
      </Alert>

      <Card>
        <Group justify="space-between" mb="md">
          <Title order={5}>{t('system.notifications.destinations')}</Title>
          <Button leftSection={<IconPlus size={16} />} onClick={openNew}>
            {t('system.notifications.add')}
          </Button>
        </Group>
        {data.destinations.length === 0 ? (
          <Text c="dimmed" size="sm">
            {t('system.notifications.empty')}
          </Text>
        ) : (
          <Stack gap="sm">
            {data.destinations.map((destination) => (
              <Card key={destination.id} withBorder>
                <Group justify="space-between" align="flex-start">
                  <Stack gap={4}>
                    <Group gap="xs">
                      <Text fw={600}>{destination.name}</Text>
                      <Badge variant="light">{destination.id}</Badge>
                      <Badge color={destination.enabled ? 'green' : 'gray'} variant="light">
                        {destination.enabled
                          ? t('system.notifications.enabled')
                          : t('system.notifications.disabled')}
                      </Badge>
                      <Badge variant="outline">{destination.mode}</Badge>
                    </Group>
                    <Text size="xs" c="dimmed">
                      {t('system.notifications.summary', {
                        severity: destination.minimumSeverity,
                        topics:
                          destination.topics.length === 0 ? '*' : destination.topics.join(', '),
                      })}
                    </Text>
                    <Text size="xs" c={destination.webhookSet ? 'green' : 'red'}>
                      {destination.webhookSet
                        ? t('system.notifications.webhookSet')
                        : t('system.notifications.webhookMissing')}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {t('system.notifications.lastDelivery', {
                        state:
                          deliveries.data?.find((item) => item.destinationId === destination.id)
                            ?.state ?? t('system.notifications.neverDelivered'),
                      })}
                    </Text>
                  </Stack>
                  <Group gap="xs">
                    <Button
                      size="xs"
                      variant="light"
                      leftSection={<IconSend size={14} />}
                      loading={test.isPending && test.variables === destination.id}
                      disabled={!destination.webhookSet}
                      onClick={() => test.mutate(destination.id)}
                    >
                      {t('system.notifications.test')}
                    </Button>
                    <Button
                      size="xs"
                      variant="light"
                      leftSection={<IconEdit size={14} />}
                      onClick={() => openEdit(destination)}
                    >
                      {t('actions.edit')}
                    </Button>
                    <Button
                      size="xs"
                      variant="light"
                      onClick={() =>
                        save.mutate(
                          data.destinations.map((item) =>
                            item.id === destination.id
                              ? toUpdate({ ...item, enabled: !item.enabled })
                              : toUpdate(item)
                          )
                        )
                      }
                      loading={save.isPending}
                    >
                      {destination.enabled
                        ? t('system.notifications.disableAction')
                        : t('system.notifications.enableAction')}
                    </Button>
                    <Button
                      size="xs"
                      variant="light"
                      color="red"
                      leftSection={<IconTrash size={14} />}
                      onClick={() => remove(destination.id)}
                      loading={save.isPending}
                    >
                      {t('actions.delete')}
                    </Button>
                  </Group>
                </Group>
              </Card>
            ))}
          </Stack>
        )}
      </Card>

      <Card>
        <Group justify="space-between" mb="sm">
          <Title order={5}>{t('system.notifications.deliveries')}</Title>
          <Button size="xs" variant="light" onClick={() => void deliveries.refetch()}>
            {t('actions.refresh')}
          </Button>
        </Group>
        {deliveries.isError ? (
          <ProblemAlert error={deliveries.error} onRetry={() => void deliveries.refetch()} />
        ) : (deliveries.data ?? []).length === 0 ? (
          <Text c="dimmed" size="sm">
            {t('system.notifications.noDeliveries')}
          </Text>
        ) : (
          <Table.ScrollContainer minWidth={900}>
            <Table highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('system.notifications.time')}</Table.Th>
                  <Table.Th>{t('system.notifications.topic')}</Table.Th>
                  <Table.Th>{t('system.notifications.destination')}</Table.Th>
                  <Table.Th>{t('system.notifications.state')}</Table.Th>
                  <Table.Th>{t('system.notifications.attempts')}</Table.Th>
                  <Table.Th>{t('system.notifications.error')}</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {(deliveries.data ?? []).map((delivery) => (
                  <DeliveryRow
                    key={delivery.id}
                    delivery={delivery}
                    onRetry={() => retry.mutate(delivery.id)}
                    retrying={retry.isPending && retry.variables === delivery.id}
                  />
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Card>

      <Modal
        opened={modalOpen}
        onClose={modal.close}
        title={
          editing && data.destinations.some((item) => item.id === editing.id)
            ? t('system.notifications.editTitle')
            : t('system.notifications.addTitle')
        }
        size="lg"
      >
        {editing && (
          <DestinationForm
            destination={editing}
            modes={data.supportedModes}
            catalog={data.catalog}
            instances={(instances.data ?? []).map((instance) => instance.id)}
            editing={data.destinations.some((item) => item.id === editing.id)}
            busy={save.isPending}
            onSave={saveOne}
            onCancel={modal.close}
          />
        )}
      </Modal>
    </Stack>
  );
}

function DeliveryRow({
  delivery,
  onRetry,
  retrying,
}: {
  delivery: NotificationDeliveryView;
  onRetry: () => void;
  retrying: boolean;
}) {
  const { t } = useTranslation();
  const color =
    delivery.state === 'sent'
      ? 'green'
      : delivery.state === 'failed'
        ? 'red'
        : delivery.state === 'dropped'
          ? 'gray'
          : 'yellow';
  return (
    <Table.Tr>
      <Table.Td>
        <Text size="xs" ff="monospace">
          {new Date(delivery.at).toLocaleString()}
        </Text>
      </Table.Td>
      <Table.Td>
        <Text size="xs">{delivery.topic}</Text>
        <Text size="xs" c="dimmed" lineClamp={1}>
          {delivery.title}
        </Text>
      </Table.Td>
      <Table.Td>{delivery.destinationId}</Table.Td>
      <Table.Td>
        <Badge color={color} variant="light">
          {delivery.state}
        </Badge>
      </Table.Td>
      <Table.Td>{delivery.attempts}</Table.Td>
      <Table.Td>
        <Text size="xs" c="red" lineClamp={2} maw={260}>
          {delivery.lastError}
        </Text>
      </Table.Td>
      <Table.Td>
        {delivery.state !== 'sent' && (
          <Button size="compact-xs" variant="light" onClick={onRetry} loading={retrying}>
            {t('actions.retry')}
          </Button>
        )}
      </Table.Td>
    </Table.Tr>
  );
}

function DestinationForm({
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
  instances: string[];
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
  const set = <K extends keyof NotificationDestinationUpdate>(
    key: K,
    next: NotificationDestinationUpdate[K]
  ) => setValue((current) => ({ ...current, [key]: next }));

  return (
    <Stack gap="sm">
      <Group grow>
        <TextInput
          label={t('system.notifications.id')}
          description={t('system.notifications.idHint')}
          required
          disabled={editing}
          value={value.id}
          onChange={(event) => set('id', event.currentTarget.value)}
        />
        <TextInput
          label={t('system.notifications.name')}
          required
          value={value.name}
          onChange={(event) => set('name', event.currentTarget.value)}
        />
      </Group>
      <PasswordInput
        label={t('system.notifications.webhook')}
        description={t('system.notifications.webhookHint')}
        placeholder={
          editing ? t('system.notifications.webhookKeep') : 'https://discord.com/api/webhooks/...'
        }
        value={value.webhookUrl ?? ''}
        onChange={(event) => set('webhookUrl', event.currentTarget.value || null)}
      />
      <Group grow>
        <Select
          label={t('system.notifications.mode')}
          data={modes}
          value={value.mode}
          onChange={(next) => set('mode', next ?? 'instant')}
          allowDeselect={false}
        />
        <Select
          label={t('system.notifications.language')}
          data={[
            { value: 'vi', label: 'Tiếng Việt' },
            { value: 'en', label: 'English' },
          ]}
          value={value.language}
          onChange={(next) => set('language', next ?? 'vi')}
          allowDeselect={false}
        />
        <Select
          label={t('system.notifications.minimumSeverity')}
          data={severities}
          value={value.minimumSeverity}
          onChange={(next) => set('minimumSeverity', next ?? 'warning')}
          allowDeselect={false}
        />
      </Group>
      <MultiSelect
        label={t('system.notifications.topics')}
        description={t('system.notifications.topicsHint')}
        data={topicData}
        searchable
        clearable
        value={value.topics ?? []}
        onChange={(next) => set('topics', next)}
      />
      <MultiSelect
        label={t('system.notifications.instances')}
        description={t('system.notifications.instancesHint')}
        data={[
          { value: '*', label: t('system.notifications.all') },
          ...instances.map((id) => ({ value: id, label: id })),
        ]}
        searchable
        clearable
        value={value.instances ?? []}
        onChange={(next) => set('instances', next)}
      />
      <Group grow>
        <TextInput
          label={t('system.notifications.mentionRole')}
          description={t('system.notifications.mentionRoleHint')}
          value={value.mentionRoleId ?? ''}
          onChange={(event) => set('mentionRoleId', event.currentTarget.value || null)}
        />
        <TextInput
          label={t('system.notifications.panelUrl')}
          placeholder="http://100.96.2.89:8765"
          value={value.panelUrl ?? ''}
          onChange={(event) => set('panelUrl', event.currentTarget.value || null)}
        />
      </Group>
      <Group grow>
        <NumberInput
          label={t('system.notifications.intervalSec')}
          min={10}
          max={3600}
          value={value.intervalSec}
          onChange={(next) => set('intervalSec', Number(next) || 60)}
        />
        <NumberInput
          label={t('system.notifications.digestSec')}
          min={30}
          max={3600}
          value={value.digestSec}
          onChange={(next) => set('digestSec', Number(next) || 300)}
        />
      </Group>
      <Switch
        label={t('system.notifications.enabled')}
        checked={value.enabled}
        onChange={(event) => set('enabled', event.currentTarget.checked)}
      />
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

function blankDestination(mode: string): NotificationDestinationUpdate {
  return {
    id: '',
    name: '',
    channel: 'discord-webhook',
    enabled: true,
    mode,
    language: 'vi',
    minimumSeverity: 'warning',
    topics: ['*'],
    instances: ['*'],
    mentionRoleId: null,
    panelUrl: null,
    intervalSec: 60,
    digestSec: 300,
    webhookUrl: null,
  };
}

function toUpdate(destination: NotificationDestinationView): NotificationDestinationUpdate {
  return {
    id: destination.id,
    name: destination.name,
    channel: destination.channel,
    enabled: destination.enabled,
    mode: destination.mode,
    language: destination.language,
    minimumSeverity: destination.minimumSeverity,
    topics: destination.topics,
    instances: destination.instances,
    mentionRoleId: destination.mentionRoleId,
    panelUrl: destination.panelUrl,
    intervalSec: destination.intervalSec,
    digestSec: destination.digestSec,
    webhookUrl: null,
  };
}
