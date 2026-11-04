import {
  IconActivity,
  IconBell,
  IconFileText,
  IconLayoutDashboard,
  IconListCheck,
  IconPlayerPlay,
  IconPlayerStop,
  IconPlus,
  IconPuzzle,
  IconRefresh,
  IconRobot,
  IconRotateClockwise,
  IconSearch,
  IconServer2,
  IconSettings,
} from '@tabler/icons-react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { notifications } from '@mantine/notifications';
import { Spotlight, type SpotlightActionData } from '@mantine/spotlight';
import type { InstanceSnapshot } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { StatusBadge } from '@/components/StatusBadge';
import { useServerCommands, type ServerCommand } from '@/features/servers/useServerCommands';

/** Command palette, Ctrl/⌘ K (docs/11 §2): go to a server or page; start, stop or restart a server. */
export function AppSpotlight() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const instances = useQuery(queries.instances());
  const { run } = useServerCommands();
  const go = (to: string, params?: Record<string, string>) => () => void navigate({ to, params });
  const servers = instances.data ?? [];
  const command = (server: InstanceSnapshot, name: ServerCommand, Icon: typeof IconSearch) => ({
    id: `${name}-${server.id}`,
    label: t(`spotlight.${name}`, { server: server.displayName }),
    description: server.id,
    leftSection: <Icon size={20} stroke={1.5} />,
    onClick: () => run(server, name),
  });

  const actions: SpotlightActionData[] = [
    ...servers.map((server) => ({
      id: `server-${server.id}`,
      label: server.displayName,
      description: server.id,
      leftSection: <IconServer2 size={20} stroke={1.5} />,
      rightSection: <StatusBadge state={server.state} size="sm" />,
      onClick: go('/servers/$id', { id: server.id }),
    })),
    // Only what the server's state allows and no job blocks, as on its buttons (docs/11 §6).
    ...servers
      .filter((server) => !server.activeJobId)
      .flatMap((server) =>
        server.state === 'running'
          ? [
              command(server, 'restart', IconRotateClockwise),
              command(server, 'stop', IconPlayerStop),
            ]
          : ['stopped', 'crashed', 'faulted'].includes(server.state)
            ? [command(server, 'start', IconPlayerPlay)]
            : []
      ),
    {
      id: 'new',
      label: t('nav.newServer'),
      leftSection: <IconPlus size={20} stroke={1.5} />,
      onClick: () => void navigate({ to: '/servers', search: { new: true } }),
    },
    {
      id: 'dashboard',
      label: t('nav.dashboard'),
      leftSection: <IconLayoutDashboard size={20} stroke={1.5} />,
      onClick: go('/'),
    },
    {
      id: 'jobs',
      label: t('nav.jobs'),
      description: t('system.tabs.jobs'),
      leftSection: <IconListCheck size={20} stroke={1.5} />,
      onClick: go('/system/jobs'),
    },
    {
      id: 'activity',
      label: t('nav.activity'),
      description: t('system.tabs.activity'),
      leftSection: <IconActivity size={20} stroke={1.5} />,
      onClick: go('/system/activity'),
    },
    {
      id: 'adapters',
      label: t('nav.adapters'),
      description: t('system.tabs.adapters'),
      leftSection: <IconPuzzle size={20} stroke={1.5} />,
      onClick: go('/system/adapters'),
    },
    {
      id: 'agents',
      label: t('nav.agents'),
      description: t('system.tabs.agents'),
      leftSection: <IconRobot size={20} stroke={1.5} />,
      onClick: go('/system/agents'),
    },
    {
      id: 'logs',
      label: t('system.tabs.logs'),
      description: t('nav.system'),
      leftSection: <IconFileText size={20} stroke={1.5} />,
      onClick: go('/system/logs'),
    },
    {
      id: 'notifications',
      label: t('system.tabs.notifications'),
      description: t('nav.system'),
      leftSection: <IconBell size={20} stroke={1.5} />,
      onClick: go('/system/notifications'),
    },
    {
      id: 'check-updates',
      label: t('system.check'),
      description: t('system.checkHint'),
      leftSection: <IconRefresh size={20} stroke={1.5} />,
      onClick: async () => {
        try {
          const check = await queryClient.query(queries.updateCheck());
          notifications.show({
            color: check.available ? 'violet' : 'green',
            message:
              check.available && check.latest
                ? t('system.available', { version: check.latest })
                : t('system.upToDate'),
          });
        } catch (error) {
          notifications.show({
            color: 'red',
            title: t('feedback.failed'),
            message: toProblem(error, t('errors.generic')).title,
          });
        }
      },
    },
    {
      id: 'system',
      label: t('nav.system'),
      description: t('system.check'),
      leftSection: <IconSettings size={20} stroke={1.5} />,
      onClick: go('/system'),
    },
  ];

  return (
    <Spotlight
      actions={actions}
      shortcut={['mod + K']}
      nothingFound="—"
      highlightQuery
      searchProps={{
        leftSection: <IconSearch size={20} stroke={1.5} />,
        placeholder: t('header.search'),
      }}
    />
  );
}
