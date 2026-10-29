import dayjs from 'dayjs';
import { useState } from 'react';
import { IconCheck, IconCloudDownload, IconRefresh } from '@tabler/icons-react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Card, Group, SegmentedControl, Stack, Text, Title } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { startUpdate, type SystemInfo } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { HelpButton } from '@/components/help/HelpButton';
import { SwitchBack } from './SwitchBack';

/** Version, the last update's result, and checking for and starting an update (docs/11 §4 System). */
export function UpdatesCard({ system }: { system: SystemInfo }) {
  const { t } = useTranslation();
  const { version, startedAt, lastUpdate } = system;
  const installed = !!system.installRoot;
  const [channel, setChannel] = useState<'stable' | 'rc'>('stable');
  const check = useQuery({ ...queries.updateCheck(channel === 'rc'), enabled: false });
  const apply = useMutation({
    mutationFn: () => startUpdate({ body: { prerelease: channel === 'rc' }, throwOnError: true }),
    onSuccess: ({ data }) =>
      notifications.show({
        color: 'violet',
        loading: true,
        message: t('system.updating', { version: data.to }),
        autoClose: 10_000,
      }),
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });
  const latest = check.data?.latest;

  return (
    <Card>
      <Group justify="space-between" align="flex-start" gap="md">
        <Stack gap={4}>
          <Title order={5}>{t('system.version')}</Title>
          <Text size="sm" c="dimmed">
            {t('system.running', { version, since: dayjs(startedAt).fromNow() })}
          </Text>
          {lastUpdate && (
            <Text size="sm" c={lastUpdate.outcome === 'succeeded' ? 'dimmed' : 'red'}>
              {t('system.lastResult')}: {lastUpdate.message} ({dayjs(lastUpdate.at).format('LLL')})
            </Text>
          )}
        </Stack>
        <Group gap="xs">
          <SegmentedControl
            size="xs"
            value={channel}
            onChange={(value) => setChannel(value as 'stable' | 'rc')}
            data={[
              { value: 'stable', label: t('system.stable') },
              { value: 'rc', label: t('system.rc') },
            ]}
            aria-label={t('system.channel')}
          />
          <HelpButton
            label={t('system.channel')}
            help={{ topicId: 'system.channel', defaultValue: 'Releases' }}
          />
          <Button
            variant="default"
            size="xs"
            leftSection={<IconRefresh size={14} />}
            loading={check.isFetching}
            onClick={() => void check.refetch()}
          >
            {t('system.check')}
          </Button>
        </Group>
      </Group>
      {!installed && (
        <Text size="sm" c="dimmed" mt="sm">
          {t('system.devBuild')}
        </Text>
      )}
      <Group mt="xs">
        <SwitchBack />
      </Group>
      {check.data && (
        <Alert
          mt="md"
          color={check.data.available ? 'violet' : 'green'}
          icon={
            check.data.available ? <IconCloudDownload aria-hidden /> : <IconCheck aria-hidden />
          }
        >
          <Group justify="space-between" gap="xs">
            <Text size="sm">
              {check.data.available && latest
                ? t('system.available', { version: latest })
                : t('system.upToDate')}
            </Text>
            {check.data.available && latest && (
              <Button
                size="xs"
                loading={apply.isPending}
                disabled={!installed}
                onClick={() =>
                  modals.openConfirmModal({
                    title: t('system.updateTitle', { version: latest }),
                    children: <Text size="sm">{t('system.updateText')}</Text>,
                    labels: {
                      confirm: t('system.update', { version: latest }),
                      cancel: t('actions.cancel'),
                    },
                    onConfirm: () => apply.mutate(),
                  })
                }
              >
                {t('system.update', { version: latest })}
              </Button>
            )}
          </Group>
        </Alert>
      )}
    </Card>
  );
}
