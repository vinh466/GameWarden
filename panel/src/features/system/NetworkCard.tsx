import { IconAlertTriangle, IconCheck, IconX } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Badge, Button, Card, Group, Modal, Stack, Text, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import type { HostCheck } from '@/api/generated';
import { queries } from '@/api/queries';
import { HelpButton } from '@/components/help/HelpButton';
import type { HelpRequest } from '@/components/help/types';
import { useHelpHost } from '@/components/help/useHelp';
import { NetworkSettingsForm } from './NetworkSettingsForm';

/**
 * Whether the download hosts resolve (an ISP's DNS can block them, docs/08 §2), and who may reach the panel and the
 * game ports, with a form to change that (docs/11 §13). The form is a Modal of its own, not `modals.open`, so it can
 * step aside while the help drawer is open over it (docs/12 §4).
 */
export function NetworkCard({ hosts }: { hosts: HostCheck[] }) {
  const { t } = useTranslation();
  const settings = useQuery(queries.systemSettings());
  const [editing, editor] = useDisclosure(false);
  const helpHost = useHelpHost();
  return (
    <Card>
      <Modal
        opened={editing}
        onClose={editor.close}
        title={t('system.networkTitle')}
        size="lg"
        {...helpHost}
      >
        <NetworkSettingsForm onSaved={editor.close} />
      </Modal>
      <Group justify="space-between" mb="sm">
        <Title order={5}>{t('system.network')}</Title>
        <Button size="xs" variant="default" onClick={editor.open}>
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
              help={{ topicId: 'system.panelNetworks' }}
            />
            <Ranges
              label={t('system.vpnRanges')}
              ranges={settings.data.vpnRanges}
              isDefault={settings.data.vpnRangesDefault}
              help={{ topicId: 'system.vpnRanges' }}
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
  help,
}: {
  label: string;
  ranges: string[];
  isDefault: boolean;
  help: HelpRequest;
}) {
  const { t } = useTranslation();
  return (
    <Group gap="xs" mt="xs">
      <Group gap={4} wrap="nowrap">
        <Text size="sm" c="dimmed">
          {label}
        </Text>
        <HelpButton label={label} help={help} />
      </Group>
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
