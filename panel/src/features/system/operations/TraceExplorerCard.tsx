import { IconGitBranch, IconRefresh } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Card, Group, Title } from '@mantine/core';
import { queries } from '@/api/queries';
import { TraceList } from './TraceList';

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
      <TraceList traces={traces.data ?? []} />
    </Card>
  );
}
