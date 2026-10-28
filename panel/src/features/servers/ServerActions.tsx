import {
  IconDotsVertical,
  IconDownload,
  IconPlayerPlay,
  IconPlayerStop,
  IconRefresh,
  IconRotateClockwise,
  IconShieldCheck,
} from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Button, Group, Menu, Tooltip } from '@mantine/core';
import type { InstanceSnapshot } from '@/api/generated';
import { DeleteServerItem } from './DeleteServerItem';
import { useServerCommands } from './useServerCommands';

/**
 * The actions of one server (docs/11 §2): the primary one as a button, the rest in a menu. Unavailable actions stay
 * visible and say why (docs/11 §6 rule 2).
 */
export function ServerActions({
  server,
  compact = false,
}: {
  server: InstanceSnapshot;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const { run, pending } = useServerCommands();
  const inFlight = pending(server);
  const busy = server.activeJobId ? t('feedback.busyReason', { job: server.activeJobId }) : null;
  const notInstalled = server.state === 'notInstalled';
  const running = server.state === 'running' || server.state === 'starting';
  const transitional = server.state === 'starting' || server.state === 'stopping';
  const size = compact ? 'xs' : 'sm';

  const primary = notInstalled ? (
    <Button
      size={size}
      leftSection={<IconDownload size={16} />}
      loading={inFlight === 'install'}
      disabled={!!busy}
      onClick={() => run(server, 'install')}
    >
      {t('actions.install')}
    </Button>
  ) : running ? (
    <Group gap="xs" wrap="nowrap">
      <Button
        size={size}
        variant="default"
        leftSection={<IconRotateClockwise size={16} />}
        loading={inFlight === 'restart'}
        disabled={!!busy || transitional}
        onClick={() => run(server, 'restart')}
      >
        {t('actions.restart')}
      </Button>
      {!compact && (
        <Button
          size={size}
          variant="light"
          color="red"
          leftSection={<IconPlayerStop size={16} />}
          loading={inFlight === 'stop' || server.state === 'stopping'}
          disabled={!!busy}
          onClick={() => run(server, 'stop')}
        >
          {t('actions.stop')}
        </Button>
      )}
    </Group>
  ) : (
    <Button
      size={size}
      leftSection={<IconPlayerPlay size={16} />}
      loading={inFlight === 'start' || server.state === 'stopping'}
      disabled={!!busy}
      onClick={() => run(server, 'start')}
    >
      {t('actions.start')}
    </Button>
  );

  // Right-aligned wherever it sits: a table cell, a card footer, a page header.
  return (
    <Group gap="xs" wrap="nowrap" justify="flex-end">
      {busy ? (
        <Tooltip label={busy}>
          <span>{primary}</span>
        </Tooltip>
      ) : (
        primary
      )}
      <Menu position="bottom-end">
        <Menu.Target>
          <ActionIcon variant="default" size={compact ? 'md' : 'lg'} aria-label={t('actions.more')}>
            <IconDotsVertical size={16} />
          </ActionIcon>
        </Menu.Target>
        <Menu.Dropdown>
          {running && compact && (
            <Menu.Item
              leftSection={<IconPlayerStop size={16} />}
              color="red"
              disabled={!!busy}
              onClick={() => run(server, 'stop')}
            >
              {t('actions.stop')}
            </Menu.Item>
          )}
          <Menu.Item
            leftSection={<IconRefresh size={16} />}
            disabled={!!busy || notInstalled}
            onClick={() => run(server, 'update')}
          >
            {t('actions.updateGame')}
          </Menu.Item>
          <Menu.Item
            leftSection={<IconShieldCheck size={16} />}
            disabled={!!busy || running}
            onClick={() => run(server, 'install')}
          >
            {t('actions.validateFiles')}
          </Menu.Item>
          <Menu.Divider />
          <DeleteServerItem server={server} disabled={!!busy || running} />
        </Menu.Dropdown>
      </Menu>
    </Group>
  );
}
