import {
  IconActivity,
  IconLayoutDashboard,
  IconListCheck,
  IconPlus,
  IconPuzzle,
  IconSearch,
  IconServer2,
  IconSettings,
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Spotlight, type SpotlightActionData } from '@mantine/spotlight';
import { queries } from '@/api/queries';
import { StatusBadge } from '@/components/StatusBadge';

/** Command palette, Ctrl/⌘ K (docs/11 §2): pages and servers. */
export function AppSpotlight() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const instances = useQuery(queries.instances());
  const go = (to: string, params?: Record<string, string>) => () => void navigate({ to, params });

  const actions: SpotlightActionData[] = [
    ...(instances.data ?? []).map((server) => ({
      id: `server-${server.id}`,
      label: server.displayName,
      description: server.id,
      leftSection: <IconServer2 size={20} stroke={1.5} />,
      rightSection: <StatusBadge state={server.state} size="sm" />,
      onClick: go('/servers/$id', { id: server.id }),
    })),
    {
      id: 'new',
      label: t('nav.newServer'),
      leftSection: <IconPlus size={20} stroke={1.5} />,
      onClick: go('/servers/new'),
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
