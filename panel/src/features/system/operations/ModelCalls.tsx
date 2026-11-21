import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Card, Code, Group, ScrollArea, Stack, Table, Text } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { modals } from '@mantine/modals';
import type { AgentModelCallRecord } from '@/api/generated';
import { formatLatency } from './agentFormat';

/** Recent model calls: a table on desktop, cards on phones (docs/22 §7.14). */
export function ModelCalls({ calls }: { calls: AgentModelCallRecord[] }) {
  const { t } = useTranslation();
  const isPhone = useMediaQuery('(max-width: 48em)');

  if (calls.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t('system.operations.agentNoCalls')}
      </Text>
    );
  }

  if (isPhone) {
    return (
      <Stack gap="xs" mt="sm">
        {calls.map((call, index) => (
          <Card key={call.id ?? index} withBorder p="sm">
            <Group justify="space-between" gap="xs" wrap="nowrap">
              <Text size="sm" fw={500}>
                {call.purpose ?? ''}
              </Text>
              <Badge color={call.status === 'error' ? 'red' : 'green'} variant="light">
                {call.status ?? 'ok'}
              </Badge>
            </Group>
            <Text size="xs" c="dimmed" mt={2}>
              {call.at ? new Date(call.at).toLocaleTimeString() : ''} · {call.model ?? ''}
            </Text>
            <Group gap="sm" mt={4}>
              <Text size="xs">{formatLatency(call.latencyMs)}</Text>
              <Text size="xs" c="dimmed">
                {(call.promptTokens ?? 0) + (call.completionTokens ?? 0)}{' '}
                {t('system.operations.agentTokens').toLowerCase()}
              </Text>
              <Text size="xs" c="dimmed">
                {call.toolCalls ?? 0} {t('system.operations.agentTools').toLowerCase()}
              </Text>
            </Group>
            <Button size="compact-xs" variant="subtle" mt={4} onClick={() => showCall(call, t)}>
              {t('system.operations.agentDetails')}
            </Button>
          </Card>
        ))}
      </Stack>
    );
  }

  return (
    <Table.ScrollContainer minWidth={720}>
      <Table verticalSpacing="xs" highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>{t('system.operations.agentTime')}</Table.Th>
            <Table.Th>{t('system.operations.agentPurpose')}</Table.Th>
            <Table.Th>{t('system.operations.agentModel')}</Table.Th>
            <Table.Th>{t('system.operations.agentCallLatency')}</Table.Th>
            <Table.Th>{t('system.operations.agentTokens')}</Table.Th>
            <Table.Th>{t('system.operations.agentTools')}</Table.Th>
            <Table.Th>{t('system.operations.agentStatus')}</Table.Th>
            <Table.Th />
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {calls.map((call, index) => (
            <Table.Tr key={call.id ?? index}>
              <Table.Td>
                <Text size="xs">{call.at ? new Date(call.at).toLocaleTimeString() : ''}</Text>
              </Table.Td>
              <Table.Td>{call.purpose ?? ''}</Table.Td>
              <Table.Td>{call.model ?? ''}</Table.Td>
              <Table.Td>{formatLatency(call.latencyMs)}</Table.Td>
              <Table.Td>{(call.promptTokens ?? 0) + (call.completionTokens ?? 0)}</Table.Td>
              <Table.Td>{call.toolCalls ?? 0}</Table.Td>
              <Table.Td>
                <Badge color={call.status === 'error' ? 'red' : 'green'} variant="light">
                  {call.status ?? 'ok'}
                </Badge>
              </Table.Td>
              <Table.Td>
                <Button size="xs" variant="subtle" onClick={() => showCall(call, t)}>
                  {t('system.operations.agentDetails')}
                </Button>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}

function showCall(call: AgentModelCallRecord, t: TFunction) {
  modals.open({
    title: `${t('system.operations.agentCallDetail')} · ${call.purpose ?? ''}`,
    size: 'xl',
    children: (
      <Stack>
        <Group gap="xs">
          <Badge color={call.status === 'error' ? 'red' : 'green'}>{call.status ?? 'ok'}</Badge>
          <Text size="sm">{call.model}</Text>
          <Text size="sm" c="dimmed">
            {formatLatency(call.latencyMs)} ·{' '}
            {(call.promptTokens ?? 0) + (call.completionTokens ?? 0)} tokens
          </Text>
        </Group>
        {call.error && (
          <Text size="sm" c="red">
            {call.error}
          </Text>
        )}
        <Text size="sm" fw={600}>
          {t('system.operations.agentPrompt')}
        </Text>
        <ScrollArea h={220}>
          <Code block style={{ whiteSpace: 'pre-wrap' }}>
            {call.promptPreview ?? '—'}
          </Code>
        </ScrollArea>
        <Text size="sm" fw={600}>
          {t('system.operations.agentResponse')}
        </Text>
        <ScrollArea h={220}>
          <Code block style={{ whiteSpace: 'pre-wrap' }}>
            {call.responsePreview ?? '—'}
          </Code>
        </ScrollArea>
        {call.traceId && (
          <Text size="xs" c="dimmed">
            trace: {call.traceId}
          </Text>
        )}
      </Stack>
    ),
  });
}
