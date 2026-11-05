import { IconChecklist, IconPlayerPlay, IconRefresh } from '@tabler/icons-react';
import { useMutation } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Card, Group, ScrollArea, Stack, Table, Text, Title } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { runAgentHostEvals, type AgentEvalCaseResult } from '@/api/generated';
import { toProblem } from '@/api/problems';

/** Golden scenarios run against the configured model (docs/21 §5.7). Explicit, never automatic. */
export function AgentQualityCard() {
  const { t } = useTranslation();
  const run = useMutation({
    mutationFn: () => runAgentHostEvals({ throwOnError: true }),
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });
  const result = run.data?.data;

  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Group gap="xs">
          <IconChecklist size={20} />
          <Title order={5}>{t('system.operations.agentQuality')}</Title>
        </Group>
        <Group gap="xs">
          {result && (
            <Badge color={result.passed === result.total ? 'green' : 'orange'} variant="light">
              {t('system.operations.agentEvalSummary', {
                passed: result.passed,
                total: result.total,
              })}
            </Badge>
          )}
          <Button
            size="xs"
            variant="light"
            leftSection={<IconPlayerPlay size={14} />}
            loading={run.isPending}
            onClick={() => run.mutate()}
          >
            {run.isPending
              ? t('system.operations.agentRunningEvals')
              : t('system.operations.agentRunEvals')}
          </Button>
        </Group>
      </Group>
      <Text size="xs" c="dimmed" mb="sm">
        {t('system.operations.agentQualityHint')}
      </Text>
      {!result ? (
        <Text size="sm" c="dimmed">
          {run.isPending
            ? t('system.operations.agentRunningEvals')
            : t('system.operations.agentEvalNoRun')}
        </Text>
      ) : (
        <>
          <Group gap="xs" mb="xs">
            <Text size="xs" c="dimmed">
              {result.model} · {new Date(result.at).toLocaleString()}
            </Text>
            <Button
              size="xs"
              variant="subtle"
              leftSection={<IconRefresh size={14} />}
              onClick={() => run.mutate()}
            >
              {t('actions.refresh')}
            </Button>
          </Group>
          <Table.ScrollContainer minWidth={720}>
            <Table verticalSpacing="xs" highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('system.operations.agentEvalScenario')}</Table.Th>
                  <Table.Th>{t('system.operations.agentStatus')}</Table.Th>
                  <Table.Th>{t('system.operations.agentEvalScore')}</Table.Th>
                  <Table.Th>{t('system.operations.agentTools')}</Table.Th>
                  <Table.Th>{t('system.operations.agentEvalAnswer')}</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {result.cases.map((item) => (
                  <Table.Tr key={item.id ?? item.name}>
                    <Table.Td>{item.name ?? item.id}</Table.Td>
                    <Table.Td>
                      <Badge color={item.passed ? 'green' : 'red'} variant="light">
                        {item.passed
                          ? t('system.operations.agentEvalPassed')
                          : t('system.operations.agentEvalFailed')}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{item.score ?? 0}%</Table.Td>
                    <Table.Td>{(item.toolCalls ?? []).join(', ') || '—'}</Table.Td>
                    <Table.Td>
                      <Text size="xs" lineClamp={2}>
                        {item.answer || item.error || '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Button size="xs" variant="subtle" onClick={() => showCase(item)}>
                        {t('system.operations.agentDetails')}
                      </Button>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </>
      )}
    </Card>
  );
}

function showCase(item: AgentEvalCaseResult) {
  modals.open({
    title: item.name ?? item.id ?? '',
    size: 'lg',
    children: (
      <ScrollArea h={420}>
        <Stack gap="xs">
          <Text size="sm" fw={500}>
            Answer
          </Text>
          <Text size="sm">{item.answer || item.error || '—'}</Text>
          <Text size="sm" fw={500}>
            Checks
          </Text>
          {(item.checks ?? []).map((check, index) => (
            <Group key={`${check.name}-${index}`} gap="xs" align="flex-start" wrap="nowrap">
              <Badge color={check.ok ? 'green' : 'red'} variant="light">
                {check.ok ? 'ok' : 'fail'}
              </Badge>
              <Stack gap={0}>
                <Text size="sm">{check.name}</Text>
                {check.detail && (
                  <Text size="xs" c="dimmed" style={{ overflowWrap: 'anywhere' }}>
                    {check.detail}
                  </Text>
                )}
              </Stack>
            </Group>
          ))}
        </Stack>
      </ScrollArea>
    ),
  });
}
