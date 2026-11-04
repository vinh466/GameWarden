import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Box,
  Card,
  Divider,
  Group,
  ScrollArea,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core';
import type { AgentAuditView } from '@/api/generated';

/** Every Agent call, denial and failure, server-side (docs/15). */
export function AgentAuditCard({ rows }: { rows: AgentAuditView[] }) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Title order={5}>{t('agents.audit')}</Title>
        <Text size="xs" c="dimmed">
          {t('agents.auditText')}
        </Text>
      </Group>
      {rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t('agents.auditEmpty')}
        </Text>
      ) : (
        <>
          <Stack gap={0} hiddenFrom="sm">
            {rows.map((row, index) => (
              <Box key={row.id}>
                <Stack gap={4} p="sm">
                  <Group justify="space-between" gap="xs" wrap="nowrap">
                    <Text
                      component="time"
                      dateTime={row.at}
                      size="xs"
                      ff="monospace"
                      title={dayjs(row.at).format('LLL')}
                    >
                      {dayjs(row.at).format('HH:mm:ss')} · {dayjs(row.at).format('YYYY-MM-DD')}
                    </Text>
                    <OutcomeBadge row={row} />
                  </Group>
                  <Text size="sm" ff="monospace" style={{ wordBreak: 'break-all' }}>
                    {row.action}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {row.clientName ?? t('agents.none')}
                  </Text>
                </Stack>
                {index < rows.length - 1 && <Divider />}
              </Box>
            ))}
          </Stack>

          <Box visibleFrom="sm">
            <ScrollArea h={320}>
              <Table highlightOnHover aria-label={t('agents.audit')}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('agents.when')}</Table.Th>
                    <Table.Th>{t('agents.client')}</Table.Th>
                    <Table.Th>{t('agents.action')}</Table.Th>
                    <Table.Th>{t('agents.outcome')}</Table.Th>
                    <Table.Th>{t('agents.ip')}</Table.Th>
                    <Table.Th ta="right">{t('agents.duration')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {rows.map((row) => (
                    <Table.Tr key={row.id}>
                      <Table.Td>
                        <Text
                          component="time"
                          dateTime={row.at}
                          size="sm"
                          title={dayjs(row.at).format('LLL')}
                        >
                          {dayjs(row.at).format('HH:mm:ss')}
                        </Text>
                      </Table.Td>
                      <Table.Td>{row.clientName ?? t('agents.none')}</Table.Td>
                      <Table.Td>
                        <Text size="sm" ff="monospace" style={{ wordBreak: 'break-all' }}>
                          {row.action}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <OutcomeBadge row={row} />
                      </Table.Td>
                      <Table.Td>{row.sourceIp ?? t('agents.none')}</Table.Td>
                      <Table.Td ta="right">{row.durationMs} ms</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          </Box>
        </>
      )}
    </Card>
  );
}

function OutcomeBadge({ row }: { row: AgentAuditView }) {
  return (
    <Badge color={row.outcome === 'ok' ? 'green' : row.outcome === 'denied' ? 'yellow' : 'red'}>
      {row.outcome} {row.statusCode}
    </Badge>
  );
}
