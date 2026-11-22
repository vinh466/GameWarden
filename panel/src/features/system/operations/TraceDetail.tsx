import { useQuery } from '@tanstack/react-query';
import type { TFunction } from 'i18next';
import { Badge, Code, Group, ScrollArea, Stack, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { RelativeTime } from '@/components/RelativeTime';
import { formatLatency } from './agentFormat';

/** The merged timeline behind one trace, opened from the trace list (docs/21 §2.9). */
export function TraceDetail({ traceId, t }: { traceId: string; t: TFunction }) {
  const trace = useQuery(queries.agentTrace(traceId));
  if (trace.isLoading) {
    return <Text size="sm">{t('system.operations.loading')}</Text>;
  }

  if (trace.isError) {
    return <ProblemAlert error={trace.error} onRetry={() => void trace.refetch()} />;
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
      detail: `${formatLatency(call.latencyMs)} · ${(call.promptTokens ?? 0) + (call.completionTokens ?? 0)} ${t('system.operations.agentTokensUnit')} · ${call.status ?? t('system.operations.agentStatusValues.ok')}`,
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
            <RelativeTime value={data.firstAt} /> → <RelativeTime value={data.lastAt} />
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
                  <RelativeTime value={item.at} />
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
