import { IconAlertTriangle, IconCheck, IconX } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Badge, Card, Group, Stack, Text, Title } from '@mantine/core';
import type { HostCheck } from '@/api/generated';

/** Whether the download hosts resolve (an ISP's DNS can block them, docs/08 §2) and the VPN ranges. */
export function NetworkCard({ hosts, vpnRanges }: { hosts: HostCheck[]; vpnRanges: string[] }) {
  const { t } = useTranslation();
  return (
    <Card>
      <Title order={5} mb="sm">
        {t('system.network')}
      </Title>
      <Stack gap="xs">
        {hosts.map((host) => (
          <HostRow key={host.host} host={host} />
        ))}
        <Group gap="xs" mt="xs">
          <Text size="sm" c="dimmed">
            {t('system.vpnRanges')}
          </Text>
          {vpnRanges.map((range) => (
            <Badge key={range} variant="default" ff="monospace" tt="none">
              {range}
            </Badge>
          ))}
        </Group>
      </Stack>
    </Card>
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
