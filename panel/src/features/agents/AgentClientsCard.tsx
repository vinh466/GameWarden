import dayjs from 'dayjs';
import { IconKey, IconRobot } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Box,
  Button,
  Card,
  Code,
  Divider,
  Group,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core';
import type { AgentClientView } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';

/** The named Agent clients, their scopes and last call; tokens open in a modal. */
export function AgentClientsCard({
  clients,
  busyId,
  onToggle,
  onTokens,
}: {
  clients: AgentClientView[];
  busyId?: string;
  onToggle: (client: AgentClientView) => void;
  onTokens: (client: AgentClientView) => void;
}) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Group gap="xs">
          <Title order={5}>{t('agents.clients')}</Title>
          <Badge variant="light">{clients.length}</Badge>
        </Group>
      </Group>
      {clients.length === 0 ? (
        <EmptyState icon={IconRobot} title={t('agents.empty')} text={t('agents.emptyText')} />
      ) : (
        <>
          <Stack gap={0} hiddenFrom="sm">
            {clients.map((client, index) => (
              <Box key={client.id}>
                <Stack gap={4} p="sm">
                  <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
                    <Stack gap={0} miw={0}>
                      <Text size="sm" fw={500}>
                        {client.name}
                      </Text>
                      <Text size="xs" c="dimmed" ff="monospace">
                        {client.id}
                      </Text>
                    </Stack>
                    <Badge color={client.enabled ? 'green' : 'gray'} variant="light">
                      {client.enabled ? t('agents.enabled') : t('agents.disabled')}
                    </Badge>
                  </Group>
                  <Permissions client={client} />
                  <Text size="xs" c="dimmed">
                    {t('agents.lastSeen')}:{' '}
                    {client.lastSeenAt ? dayjs(client.lastSeenAt).fromNow() : t('agents.never')}
                  </Text>
                  <ClientActions
                    client={client}
                    busy={busyId === client.id}
                    justify="flex-start"
                    onToggle={onToggle}
                    onTokens={onTokens}
                  />
                </Stack>
                {index < clients.length - 1 && <Divider />}
              </Box>
            ))}
          </Stack>

          <Box visibleFrom="sm">
            <Table.ScrollContainer minWidth={900}>
              <Table highlightOnHover aria-label={t('agents.clients')}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('agents.name')}</Table.Th>
                    <Table.Th w={120}>{t('agents.status')}</Table.Th>
                    <Table.Th>{t('agents.permissions')}</Table.Th>
                    <Table.Th w={150}>{t('agents.lastSeen')}</Table.Th>
                    <Table.Th w={180} />
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {clients.map((client) => (
                    <Table.Tr key={client.id}>
                      <Table.Td>
                        <Stack gap={0}>
                          <Text size="sm" fw={500}>
                            {client.name}
                          </Text>
                          <Text size="xs" c="dimmed" ff="monospace">
                            {client.id}
                          </Text>
                        </Stack>
                      </Table.Td>
                      <Table.Td>
                        <Badge
                          color={client.enabled ? 'green' : 'gray'}
                          variant="light"
                          style={{ whiteSpace: 'nowrap' }}
                        >
                          {client.enabled ? t('agents.enabled') : t('agents.disabled')}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Permissions client={client} />
                      </Table.Td>
                      <Table.Td>
                        {client.lastSeenAt ? (
                          <Text
                            component="time"
                            dateTime={client.lastSeenAt}
                            size="sm"
                            c="dimmed"
                            title={dayjs(client.lastSeenAt).format('LLL')}
                          >
                            {dayjs(client.lastSeenAt).fromNow()}
                          </Text>
                        ) : (
                          <Text size="sm" c="dimmed">
                            {t('agents.never')}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <ClientActions
                          client={client}
                          busy={busyId === client.id}
                          justify="flex-end"
                          onToggle={onToggle}
                          onTokens={onTokens}
                        />
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          </Box>
        </>
      )}
    </Card>
  );
}

/** The actual scopes, not just a count: the Owner sees what the client may call (docs/15). */
function Permissions({ client }: { client: AgentClientView }) {
  const { t } = useTranslation();
  if (client.trustedFull) {
    return (
      <Badge color="red" variant="light">
        {t('agents.trustedFull')}
      </Badge>
    );
  }

  const shown = client.permissions.slice(0, 3);
  return (
    <Group gap={4} wrap="wrap">
      {shown.map((permission) => (
        <Code key={permission}>{permission}</Code>
      ))}
      {client.permissions.length > shown.length && (
        <Text size="xs" c="dimmed">
          +{client.permissions.length - shown.length}
        </Text>
      )}
    </Group>
  );
}

function ClientActions({
  client,
  busy,
  justify,
  onToggle,
  onTokens,
}: {
  client: AgentClientView;
  busy: boolean;
  justify: 'flex-start' | 'flex-end';
  onToggle: (client: AgentClientView) => void;
  onTokens: (client: AgentClientView) => void;
}) {
  const { t } = useTranslation();
  return (
    <Group gap="xs" justify={justify} wrap="nowrap">
      <Button
        size="compact-xs"
        variant="default"
        leftSection={<IconKey size={12} />}
        onClick={() => onTokens(client)}
      >
        {t('agents.tokens')}
      </Button>
      <Button
        size="compact-xs"
        variant={client.enabled ? 'light' : 'filled'}
        color={client.enabled ? 'gray' : 'green'}
        loading={busy}
        onClick={() => onToggle(client)}
      >
        {client.enabled ? t('agents.disable') : t('agents.enable')}
      </Button>
    </Group>
  );
}
