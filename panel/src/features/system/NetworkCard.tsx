import { IconAlertTriangle, IconCheck, IconX } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Badge, Button, Card, Group, Stack, Text, Title } from '@mantine/core';
import { modals } from '@mantine/modals';
import type { HostCheck } from '@/api/generated';
import { queries } from '@/api/queries';
import { NetworkSettingsForm } from './NetworkSettingsForm';

/**
 * Whether the download hosts resolve (an ISP's DNS can block them, docs/08 §2), and who may reach the panel and the
 * game ports, with a form to change that (docs/11 §13).
 */
export function NetworkCard({ hosts }: { hosts: HostCheck[] }) {
  const { t } = useTranslation();
  const settings = useQuery(queries.systemSettings());
  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Title order={5}>{t('system.network')}</Title>
        <Button
          size="xs"
          variant="default"
          onClick={() => {
            const id = modals.open({
              title: t('system.networkTitle'),
              size: 'lg',
              children: <NetworkSettingsForm onSaved={() => modals.close(id)} />,
            });
          }}
        >
          {t('system.change')}
        </Button>
      </Group>
      <Stack gap="xs">
        {hosts.map((host) => (
          <HostRow key={host.host} host={host} />
        ))}
        {settings.data && (
          <>
            <Ranges
              label={t('system.panelNetworks')}
              ranges={settings.data.panelAccessNetworks}
              isDefault={settings.data.panelAccessNetworksDefault}
            />
            <Ranges
              label={t('system.vpnRanges')}
              ranges={settings.data.vpnRanges}
              isDefault={settings.data.vpnRangesDefault}
            />
          </>
        )}
      </Stack>
    </Card>
  );
}

function Ranges({
  label,
  ranges,
  isDefault,
}: {
  label: string;
  ranges: string[];
  isDefault: boolean;
}) {
  const { t } = useTranslation();
  return (
    <Group gap="xs" mt="xs">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      {ranges.map((range) => (
        <Badge key={range} variant="default" ff="monospace" tt="none">
          {range}
        </Badge>
      ))}
      {isDefault && (
        <Text size="xs" c="dimmed">
          ({t('system.default')})
        </Text>
      )}
    </Group>
  );
}

function HostRow({ host }: { host: HostCheck }) {
  const { t } = useTranslation();
  const ok = host.status === 'ok';
  const label = ok
    ? t('system.dnsOk')
    : host.status === 'blocked'
      ? t('system.dnsBlocked')
      : t('system.dnsUnresolved');
  return (
    <Stack gap={2}>
      <Group justify="space-between" wrap="nowrap" gap="xs">
        <Stack gap={0} miw={0}>
          <Text size="sm" ff="monospace" truncate>
            {host.host}
          </Text>
          <Text size="xs" c="dimmed">
            {host.purpose}
          </Text>
        </Stack>
        <Badge
          color={ok ? 'green' : 'red'}
          leftSection={ok ? <IconCheck size={12} /> : <IconX size={12} />}
        >
          {label}
        </Badge>
      </Group>
      {host.detail && (
        <Alert color="red" p="xs" icon={<IconAlertTriangle size={16} aria-hidden />}>
          <Text size="xs">{host.detail}</Text>
        </Alert>
      )}
    </Stack>
  );
}
