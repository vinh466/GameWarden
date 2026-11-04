import { useState } from 'react';
import { IconAlertTriangle } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Card, Code, Group, Modal, Skeleton, Stack, Text, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import {
  retryNotificationDelivery,
  testNotificationDestination,
  updateNotificationSettings,
  type NotificationDestinationUpdate,
  type NotificationDestinationView,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';
import { DeliveryLogCard } from './DeliveryLogCard';
import { DestinationForm } from './DestinationForm';
import { DestinationsCard } from './DestinationsCard';
import { blankDestination, toUpdate } from './notifications';

/** Discord destinations and their delivery log, on the System screen (docs/18). */
export function NotificationsScreen() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const settings = useQuery(queries.notificationSettings());
  const deliveries = useQuery(queries.notificationDeliveries());
  const instances = useQuery(queries.instances());
  const statusPreview = useQuery(queries.notificationStatusPreview());
  const [editing, setEditing] = useState<NotificationDestinationUpdate | null>(null);
  const [modalOpen, modal] = useDisclosure(false);
  const [refreshing, setRefreshing] = useState(false);

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
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  if (settings.isError) {
    return (
      <SystemTab description={t('system.notifications.description')}>
        <ProblemAlert error={settings.error} onRetry={() => void settings.refetch()} />
      </SystemTab>
    );
  }

  if (!settings.data) {
    return (
      <SystemTab description={t('system.notifications.description')}>
        <Skeleton h={260} />
      </SystemTab>
    );
  }

  const data = settings.data;
  const openNew = () => {
    save.reset();
    setEditing(blankDestination(data.supportedModes[0] ?? 'instant'));
    modal.open();
  };
  const openEdit = (destination: NotificationDestinationView) => {
    save.reset();
    setEditing(toUpdate(destination));
    modal.open();
  };
  const saveOne = (destination: NotificationDestinationUpdate) => {
    const current = data.destinations.map(toUpdate);
    const next = current.some((item) => item.id === destination.id)
      ? current.map((item) => (item.id === destination.id ? destination : item))
      : [...current, destination];
    // Keep the form open when saving fails: closing it would lose what the Owner typed.
    save.mutate(next, { onSuccess: modal.close });
  };
  const remove = (id: string) => {
    save.mutate(data.destinations.filter((destination) => destination.id !== id).map(toUpdate));
  };
  const confirmRemove = (destination: NotificationDestinationView) => {
    modals.openConfirmModal({
      title: t('system.notifications.deleteTitle', { name: destination.name }),
      children: <Text size="sm">{t('system.notifications.deleteText')}</Text>,
      labels: { confirm: t('actions.delete'), cancel: t('actions.cancel') },
      confirmProps: { color: 'red' },
      onConfirm: () => remove(destination.id),
    });
  };
  const toggle = (destination: NotificationDestinationView) => {
    save.mutate(
      data.destinations.map((item) =>
        item.id === destination.id ? toUpdate({ ...item, enabled: !item.enabled }) : toUpdate(item)
      )
    );
  };
  const refreshDeliveries = async () => {
    setRefreshing(true);
    try {
      await deliveries.refetch();
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <SystemTab description={t('system.notifications.description')}>
      <Stack gap="lg">
        <Alert color="blue" icon={<IconAlertTriangle />} title={t('system.notifications.pinTitle')}>
          {t('system.notifications.pinText')}
        </Alert>
        {data.destinations.some((destination) => destination.mode === 'status') && (
          <Card>
            <Group justify="space-between" mb="sm">
              <Title order={5}>{t('system.notifications.statusPreview')}</Title>
              <Text size="xs" c="dimmed">
                {t('system.notifications.statusPreviewHint')}
              </Text>
            </Group>
            {statusPreview.isError ? (
              <ProblemAlert
                error={statusPreview.error}
                onRetry={() => void statusPreview.refetch()}
              />
            ) : !statusPreview.data ? (
              <Skeleton h={80} />
            ) : (
              <Code block fz="xs" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                {statusPreview.data.text}
              </Code>
            )}
          </Card>
        )}
        <DestinationsCard
          destinations={data.destinations}
          deliveries={deliveries.data ?? []}
          busy={save.isPending}
          testingId={test.isPending ? test.variables : undefined}
          onAdd={openNew}
          onEdit={openEdit}
          onTest={(destination) => test.mutate(destination.id)}
          onToggle={toggle}
          onDelete={confirmRemove}
        />
        <DeliveryLogCard
          deliveries={deliveries.data}
          error={deliveries.isError ? deliveries.error : null}
          loading={refreshing}
          retryingId={retry.isPending ? retry.variables : undefined}
          onRetry={(delivery) => retry.mutate(delivery.id)}
          onRefresh={() => void refreshDeliveries()}
        />
      </Stack>

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
        <Stack gap="md">
          {save.isError && <ProblemAlert error={save.error} />}
          {editing && (
            <DestinationForm
              destination={editing}
              modes={data.supportedModes}
              catalog={data.catalog}
              instances={(instances.data ?? []).map((instance) => ({
                id: instance.id,
                label: instance.displayName,
                state: instance.state,
              }))}
              editing={data.destinations.some((item) => item.id === editing.id)}
              busy={save.isPending}
              onSave={saveOne}
              onCancel={modal.close}
            />
          )}
        </Stack>
      </Modal>
    </SystemTab>
  );
}
