import { IconGitBranch, IconRefresh } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Button,
  Card,
  Code,
  Group,
  ScrollArea,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core';
import { modals } from '@mantine/modals';
import { queries } from '@/api/queries';

/** Recent traces and a merged timeline of model calls, tools, policy, approvals and incidents (docs/21 §2.9). */
export function TraceExplorerCard() {
  const { t } = useTranslation();
  const traces = useQuery(queries.agentTraces(20));

  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Group gap="xs">
          <IconGitBranch size={20} />
          <Title order={5}>{t('system.operations.agentTraces')}</Title>
        </Group>
        <Button
          variant="subtle"
          size="xs"
          leftSection={<IconRefresh size={14} />}
          loading={traces.isFetching}
          onClick={() => void traces.refetch()}
        >
          {t('actions.refresh')}
        </Button>
      </Group>
      {traces.data?.length ? (
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
              {traces.data.map((trace, index) => (
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
      ) : (
        <Text size="sm" c="dimmed">
          {t('system.operations.agentTraceEmpty')}
        </Text>
      )}
    </Card>
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

function TraceDetail({ traceId, t }: { traceId: string; t: TFunction }) {
  const trace = useQuery(queries.agentTrace(traceId));
  if (trace.isLoading) {
    return <Text size="sm">{t('system.operations.loading')}</Text>;
  }

  if (!trace.data) {
    return <Text size="sm">{t('system.operations.agentTraceEmpty')}</Text>;
  }

  const data = trace.data;
  const items = [
    ...(data.modelCalls ?? []).map((call) => ({
      at: call.at ?? '',
      kind: t('system.operations.agentTraceModel'),
      title: `${call.purpose ?? ''} · ${call.model ?? ''}`,
      detail: `${formatLatency(call.latencyMs)} · ${(call.promptTokens ?? 0) + (call.completionTokens ?? 0)} tokens · ${call.status ?? 'ok'}`,
      code:
        call.error ??
        ([
          call.promptPreview ? `REQUEST:\n${call.promptPreview}` : null,
          call.responsePreview ? `RESPONSE:\n${call.responsePreview}` : null,
        ]
          .filter(Boolean)
          .join('\n\n') ||
          null),
    })),
    ...(data.toolCalls ?? []).map((call) => ({
      at: call.createdAt ?? '',
      kind: t('system.operations.agentTraceTool'),
      title: `${call.tool ?? ''} · ${call.state ?? ''}`,
      detail: `${call.durationMs ?? 0} ms`,
      code: call.result ?? null,
    })),
    ...(data.policyDecisions ?? []).map((decision) => ({
      at: decision.at ?? '',
      kind: t('system.operations.agentTracePolicy'),
      title: `${decision.capability ?? ''} · ${decision.decision ?? ''}`,
      detail: decision.rule ?? '',
      code: decision.reason ?? null,
    })),
    ...(data.approvals ?? []).map((approval) => ({
      at: approval.requestedAt ?? '',
      kind: t('system.operations.agentTraceApproval'),
      title: `${approval.tool ?? ''} · ${approval.state ?? ''}`,
      detail: approval.decidedBy ?? '',
      code: approval.verification ?? approval.impact ?? null,
    })),
    ...(data.events ?? []).map((event) => ({
      at: event.at ?? '',
      kind: t('system.operations.agentTraceEvent'),
      title: event.type ?? '',
      detail: event.text ?? '',
      code: null,
    })),
    ...(data.incidents ?? []).map((incident) => ({
      at: incident.openedAt ?? '',
      kind: t('system.operations.agentTraceIncident'),
      title: incident.title ?? '',
      detail: incident.state ?? '',
      code: incident.summary ?? null,
    })),
    ...(data.incidentEvents ?? []).map((event) => ({
      at: event.at ?? '',
      kind: t('system.operations.agentTraceIncident'),
      title: event.type ?? '',
      detail: event.text ?? '',
      code: null,
    })),
  ].sort((left, right) => left.at.localeCompare(right.at));

  return (
    <ScrollArea h={520}>
      <Stack gap="sm">
        <Group gap="xs">
          <Text size="xs" c="dimmed">
            {data.firstAt ? new Date(data.firstAt).toLocaleString() : ''} →{' '}
            {data.lastAt ? new Date(data.lastAt).toLocaleString() : ''}
          </Text>
          {data.instanceId && <Badge variant="light">{data.instanceId}</Badge>}
        </Group>
        {items.length === 0 ? (
          <Text size="sm">{t('system.operations.agentTraceEmpty')}</Text>
        ) : (
          items.map((item, index) => (
            <Stack key={`${item.kind}-${index}`} gap={2}>
              <Group gap="xs">
                <Badge size="sm" variant="light">
                  {item.kind}
                </Badge>
                <Text size="xs" c="dimmed">
                  {item.at ? new Date(item.at).toLocaleTimeString() : ''}
                </Text>
                <Text size="sm" fw={500}>
                  {item.title}
                </Text>
              </Group>
              <Text size="xs">{item.detail}</Text>
              {item.code && (
                <Code block style={{ whiteSpace: 'pre-wrap', maxHeight: 160, overflow: 'auto' }}>
                  {item.code}
                </Code>
              )}
            </Stack>
          ))
        )}
      </Stack>
    </ScrollArea>
  );
}

function formatLatency(value?: number) {
  if (value === undefined || value === null) {
    return '—';
  }
  return value < 1000 ? `${Math.round(value)} ms` : `${(value / 1000).toFixed(1)} s`;
}
