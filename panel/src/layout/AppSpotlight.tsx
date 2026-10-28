import {
  IconActivity,
  IconLayoutDashboard,
  IconListCheck,
  IconPlayerPlay,
  IconPlayerStop,
  IconPlus,
  IconPuzzle,
  IconRotateClockwise,
  IconSearch,
  IconServer2,
  IconSettings,
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Spotlight, type SpotlightActionData } from '@mantine/spotlight';
import type { InstanceSnapshot } from '@/api/generated';
import { queries } from '@/api/queries';
import { StatusBadge } from '@/components/StatusBadge';
import { useServerCommands, type ServerCommand } from '@/features/servers/useServerCommands';

/** Command palette, Ctrl/⌘ K (docs/11 §2): go to a server or page; start, stop or restart a server. */
export function AppSpotlight() {
  const { t } = useTranslation();
  const navigate = useNavigate();
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
      leftSection: <IconListCheck size={20} stroke={1.5} />,
      onClick: go('/jobs'),
    },
    {
      id: 'activity',
      label: t('nav.activity'),
      leftSection: <IconActivity size={20} stroke={1.5} />,
      onClick: go('/activity'),
    },
    {
      id: 'adapters',
      label: t('nav.adapters'),
      leftSection: <IconPuzzle size={20} stroke={1.5} />,
      onClick: go('/adapters'),
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
