import { useTranslation } from 'react-i18next';
import { Badge, Card, Group, Stack, Text } from '@mantine/core';
import type { InstanceSnapshot } from '@/api/generated';
import { AnchorLink } from '@/components/links';
import { StatusBadge } from '@/components/StatusBadge';
import { portsLabel } from './ports';
import { ServerActions } from './ServerActions';

/**
 * The server list on phones (docs/22 §7.2): a card keeps the full state word and the primary action, instead of the
 * table squeezing both into 390 px.
 */
export function ServersMobileList({
  servers,
  adapterName,
  usage,
}: {
  servers: InstanceSnapshot[];
  adapterName: (id: string) => string;
  usage: (id: string) => string | null;
}) {
  const { t } = useTranslation();

  return (
    <Stack gap="xs">
      {servers.map((server) => (
        <Card key={server.id} withBorder p="sm">
          <Stack gap="xs">
            <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
              <Stack gap={0} miw={0}>
                <AnchorLink to="/servers/$id" params={{ id: server.id }} fw={600} truncate>
                  {server.displayName}
                </AnchorLink>
                <Text size="xs" c="dimmed" ff="monospace" truncate>
                  {server.id}
                </Text>
              </Stack>
              <Group gap={4} wrap="nowrap">
                {server.activeJobId && (
                  <Badge color="yellow" variant="light" size="sm">
                    {t('state.busy')}
                  </Badge>
                )}
                <StatusBadge state={server.state} />
              </Group>
            </Group>
            <Text size="sm" c="dimmed">
              {[adapterName(server.adapterId), portsLabel(server), usage(server.id)]
                .filter(Boolean)
                .join(' · ')}
            </Text>
            <ServerActions server={server} compact />
          </Stack>
        </Card>
      ))}
    </Stack>
  );
}
