import {
  IconActivity,
  IconLayoutDashboard,
  IconListCheck,
  IconPuzzle,
  IconRobot,
  IconServer2,
  IconSettings,
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Box, ColorSwatch, Divider, ScrollArea, Stack, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { NavLinkLink } from '@/components/links';
import { stateLook } from '@/components/StatusBadge';

const icon = (Icon: typeof IconSettings) => <Icon size={18} stroke={1.75} />;

export function Navbar({ onNavigate }: { onNavigate: () => void }) {
  const { t } = useTranslation();
  const instances = useQuery(queries.instances());
  const system = useQuery(queries.system());

  return (
    <Stack gap={0} h="100%">
      <ScrollArea flex={1} type="scroll">
        <Stack gap={2}>
          <NavLinkLink
            to="/"
            activeOptions={{ exact: true }}
            label={t('nav.dashboard')}
            leftSection={icon(IconLayoutDashboard)}
            onClick={onNavigate}
          />
          <NavLinkLink
            to="/servers"
            activeOptions={{ exact: true }}
            label={t('nav.servers')}
            leftSection={icon(IconServer2)}
            onClick={onNavigate}
          />
          <Box pl="md">
            {instances.data?.map((server) => (
              <NavLinkLink
                key={server.id}
                to="/servers/$id"
                params={{ id: server.id }}
                label={server.displayName}
                leftSection={
                  <ColorSwatch
                    color={`var(--mantine-color-${stateLook[server.state].color}-filled)`}
                    size={10}
                    withShadow={false}
                  />
                }
                onClick={onNavigate}
                py={4}
              />
            ))}
          </Box>
          <NavLinkLink
            to="/jobs"
            label={t('nav.jobs')}
            leftSection={icon(IconListCheck)}
            onClick={onNavigate}
          />
          <NavLinkLink
            to="/activity"
            label={t('nav.activity')}
            leftSection={icon(IconActivity)}
            onClick={onNavigate}
          />
          <NavLinkLink
            to="/adapters"
            label={t('nav.adapters')}
            leftSection={icon(IconPuzzle)}
            onClick={onNavigate}
          />
          <NavLinkLink
            to="/agents"
            label={t('nav.agents')}
            leftSection={icon(IconRobot)}
            onClick={onNavigate}
          />
          <NavLinkLink
            to="/system"
            label={t('nav.system')}
            leftSection={icon(IconSettings)}
            onClick={onNavigate}
          />
        </Stack>
      </ScrollArea>
      <Divider my="xs" />
      <Text size="xs" c="dimmed" px="sm" pb={4} ff="monospace">
        GameWarden {system.data ? t('core.version', { version: system.data.version }) : ''}
      </Text>
    </Stack>
  );
}
