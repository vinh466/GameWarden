import { IconAlertCircle } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Badge, Card, Group, SimpleGrid, Skeleton, Text, Title } from '@mantine/core';
import type { ResourceAmounts } from '@/api/generated';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';
import {
  CopyIconButton,
  DefinitionList,
  type DefinitionItem,
} from '@/features/servers/info/DefinitionList';

export const Route = createFileRoute('/_app/servers/$id/info')({
  component: Info,
});

function Info() {
  const { t } = useTranslation();
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const adapters = useQuery(queries.adapters());
  const system = useQuery(queries.system());
  const adapter = adapters.data?.adapters.find(
    (candidate) => candidate.id === server.data?.adapterId
  );
  const failed = [server, adapters, system].find((query) => query.isError);
  if (failed) {
    return (
      <ProblemAlert
        error={failed.error}
        onRetry={() => {
          void server.refetch();
          void adapters.refetch();
          void system.refetch();
        }}
      />
    );
  }

  if (!server.data || adapters.isLoading) {
    return <Skeleton h={240} />;
  }

  if (!adapter) {
    return <EmptyState icon={IconAlertCircle} title={t('errors.generic')} />;
  }

  const details: DefinitionItem[] = [
    {
      label: t('server.id'),
      value: (
        <Text ff="monospace" size="sm">
          {server.data.id}
        </Text>
      ),
      copy: server.data.id,
    },
    { label: t('wizard.steps.game'), value: `${adapter.name} ${adapter.version}` },
    {
      label: t('adapters.level'),
      value: <Badge variant="outline">{adapter.level}</Badge>,
    },
    { label: t('adapters.source'), value: adapter.sourceType ?? '—' },
    {
      label: t('adapters.origin'),
      value: adapter.isBuiltIn ? t('adapters.builtIn') : t('adapters.owner'),
    },
    { label: t('system.prerequisites'), value: adapter.prerequisites.join(', ') || '—' },
  ];
  const checks: DefinitionItem[] = [
    { label: t('server.minimum'), value: <ResourceBadges amounts={adapter.minResources} /> },
    {
      label: t('server.recommended'),
      value: <ResourceBadges amounts={adapter.recommendedResources} />,
    },
    { label: t('settings.title'), value: adapter.requiredSettings.join(', ') || '—' },
    { label: t('settings.secrets'), value: adapter.requiredSecrets.join(', ') || '—' },
    {
      label: t('server.ports'),
      value: adapter.ports
        .map(
          (port) =>
            `${port.name} ${port.protocol}${port.internal ? ` (${t('server.internal')})` : ''}`
        )
        .join(', '),
    },
  ];
  const launch = server.data.launchSummary ?? '—';
  const paths: DefinitionItem[] = [
    {
      label: t('server.pathServer'),
      value: pathText(instancePath(system.data?.dataRoot, server.data.id, 'server')),
      copy: instancePath(system.data?.dataRoot, server.data.id, 'server'),
    },
    {
      label: t('server.pathData'),
      value: pathText(instancePath(system.data?.dataRoot, server.data.id, 'data')),
      copy: instancePath(system.data?.dataRoot, server.data.id, 'data'),
    },
    {
      label: t('server.pathConfig'),
      value: pathText(instancePath(system.data?.dataRoot, server.data.id, 'config')),
      copy: instancePath(system.data?.dataRoot, server.data.id, 'config'),
    },
    {
      label: t('server.pathMods'),
      value: pathText(instancePath(system.data?.dataRoot, server.data.id, 'mods')),
      copy: instancePath(system.data?.dataRoot, server.data.id, 'mods'),
    },
    {
      label: t('server.pathLogs'),
      value: pathText(instancePath(system.data?.dataRoot, server.data.id, 'logs')),
      copy: instancePath(system.data?.dataRoot, server.data.id, 'logs'),
    },
  ];

  return (
    <SimpleGrid cols={{ base: 1, md: 2 }}>
      <Card>
        <Title order={5} mb="sm">
          {t('server.details')}
        </Title>
        <DefinitionList items={details} />
      </Card>
      <Card>
        <Title order={5} mb="sm">
          {t('wizard.review')}
        </Title>
        <DefinitionList items={checks} />
      </Card>
      <Card style={{ gridColumn: '1 / -1' }}>
        <Group justify="space-between" align="center" mb="sm">
          <Title order={5}>{t('server.launchSummary')}</Title>
          <CopyIconButton value={launch} label={t('actions.copy')} />
        </Group>
        <Text ff="monospace" size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
          {launch}
        </Text>
        <Text size="xs" c="dimmed" mt={4}>
          {t('server.launchSummaryHint')}
        </Text>
      </Card>
      <Card style={{ gridColumn: '1 / -1' }}>
        <Title order={5} mb="sm">
          {t('server.paths')}
        </Title>
        <DefinitionList items={paths} />
      </Card>
    </SimpleGrid>
  );
}

function pathText(value: string) {
  return (
    <Text ff="monospace" size="sm" truncate title={value}>
      {value}
    </Text>
  );
}

function instancePath(dataRoot: string | undefined, id: string, folder: string) {
  return dataRoot ? `${dataRoot}\\instances\\${id}\\${folder}` : '—';
}

function ResourceBadges({ amounts }: { amounts: ResourceAmounts | null }) {
  const { t } = useTranslation();
  if (!amounts) {
    return (
      <Text size="sm" c="dimmed">
        —
      </Text>
    );
  }

  return (
    <Group gap={6}>
      {amounts.cpuCores != null && (
        <Badge variant="default">{t('server.unitCpu', { count: amounts.cpuCores })}</Badge>
      )}
      {amounts.ramMb != null && (
        <Badge variant="default">{t('server.unitRam', { value: amounts.ramMb })}</Badge>
      )}
      {amounts.diskMb != null && (
        <Badge variant="default">{t('server.unitDisk', { value: amounts.diskMb })}</Badge>
      )}
    </Group>
  );
}
