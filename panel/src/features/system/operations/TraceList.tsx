import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Button, Card, Group, Stack, Table, Text } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { modals } from '@mantine/modals';
import type { AgentTraceSummary } from '@/api/generated';
import { TraceDetail } from './TraceDetail';

/** Recent traces: a table on desktop, cards on phones (docs/22 §7.14). */
export function TraceList({ traces }: { traces: AgentTraceSummary[] }) {
  const { t } = useTranslation();
  const isPhone = useMediaQuery('(max-width: 48em)');

  if (traces.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t('system.operations.agentTraceEmpty')}
      </Text>
    );
  }

  if (isPhone) {
    return (
      <Stack gap="xs">
        {traces.map((trace, index) => (
          <Card key={trace.traceId ?? index} withBorder p="sm">
            <Group justify="space-between" gap="xs" wrap="nowrap">
              <Text size="xs" c="dimmed">
                {trace.at ? new Date(trace.at).toLocaleString() : ''}
              </Text>
              <Button
                size="compact-xs"
                variant="subtle"
                onClick={() => showTrace(trace.traceId ?? '', t)}
              >
                {t('system.operations.agentTraceOpen')}
              </Button>
            </Group>
            <Text size="sm" fw={500} mt={2}>
              {trace.instanceId ?? 'system'} · {trace.events ?? 0}{' '}
              {t('system.operations.agentTraceEvents').toLowerCase()}
            </Text>
            <Text size="xs" c="dimmed" lineClamp={2}>
              {trace.lastEvent ?? ''}
            </Text>
          </Card>
        ))}
      </Stack>
    );
  }

  return (
    <Table.ScrollContainer minWidth={640}>
      <Table verticalSpacing="xs" highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>{t('system.operations.agentTraceTime')}</Table.Th>
            <Table.Th>{t('system.operations.agentTraceInstance')}</Table.Th>
            <Table.Th>{t('system.operations.agentTraceEvents')}</Table.Th>
            <Table.Th>{t('system.operations.agentTraceLast')}</Table.Th>
            <Table.Th />
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {traces.map((trace, index) => (
            <Table.Tr key={trace.traceId ?? index}>
              <Table.Td>
                <Text size="xs">{trace.at ? new Date(trace.at).toLocaleString() : ''}</Text>
              </Table.Td>
              <Table.Td>{trace.instanceId ?? 'system'}</Table.Td>
              <Table.Td>{trace.events ?? 0}</Table.Td>
              <Table.Td>
                <Text size="sm" lineClamp={1}>
                  {trace.lastEvent ?? ''}
                </Text>
              </Table.Td>
              <Table.Td>
                <Button
                  size="xs"
                  variant="subtle"
                  onClick={() => showTrace(trace.traceId ?? '', t)}
                >
                  {t('system.operations.agentTraceOpen')}
                </Button>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}

function showTrace(traceId: string, t: TFunction) {
  if (!traceId) {
    return;
  }

  modals.open({
    title: `${t('system.operations.agentTraceDetail')} · ${traceId.slice(0, 12)}`,
    size: 'xl',
    children: <TraceDetail traceId={traceId} t={t} />,
  });
}
