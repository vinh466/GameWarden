import { IconAlertCircle } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Badge, Card, Group, SimpleGrid, Skeleton, Table, Text, Title } from '@mantine/core';
import type { ResourceAmounts } from '@/api/generated';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';

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

  return (
    <SimpleGrid cols={{ base: 1, md: 2 }}>
      <Card>
        <Title order={5} mb="sm">
          {t('server.adapter')}
        </Title>
        <Table withRowBorders={false} highlightOnHover={false} verticalSpacing={4}>
          <Table.Tbody>
            <Row
              label="Id"
              value={
                <Text ff="monospace" size="sm">
                  {server.data.id}
                </Text>
              }
            />
            <Row label={t('server.adapter')} value={`${adapter.name} ${adapter.version}`} />
            <Row
              label={t('adapters.level')}
              value={<Badge variant="outline">{adapter.level}</Badge>}
            />
            <Row label={t('adapters.source')} value={adapter.sourceType ?? '—'} />
            <Row
              label={t('adapters.origin')}
              value={adapter.isBuiltIn ? t('adapters.builtIn') : t('adapters.owner')}
            />
            <Row
              label={t('system.prerequisites')}
              value={adapter.prerequisites.join(', ') || '—'}
            />
          </Table.Tbody>
        </Table>
      </Card>
      <Card>
        <Title order={5} mb="sm">
          {t('wizard.review')}
        </Title>
        <Table withRowBorders={false} highlightOnHover={false} verticalSpacing={4}>
          <Table.Tbody>
            <Row label="min" value={resources(adapter.minResources)} />
            <Row label="recommended" value={resources(adapter.recommendedResources)} />
            <Row label={t('settings.title')} value={adapter.requiredSettings.join(', ') || '—'} />
            <Row label={t('settings.secrets')} value={adapter.requiredSecrets.join(', ') || '—'} />
            <Row
              label={t('server.ports')}
              value={adapter.ports
                .map(
                  (port) =>
                    `${port.name} ${port.protocol}${port.internal ? ` (${t('server.internal')})` : ''}`
                )
                .join(', ')}
            />
          </Table.Tbody>
        </Table>
      </Card>
      <Card style={{ gridColumn: '1 / -1' }}>
        <Title order={5} mb="sm">
          {t('server.paths')}
        </Title>
        <Table withRowBorders={false} highlightOnHover={false} verticalSpacing={4}>
          <Table.Tbody>
            <PathRow
              label={t('server.pathServer')}
              value={instancePath(system.data?.dataRoot, server.data.id, 'server')}
            />
            <PathRow
              label={t('server.pathData')}
              value={instancePath(system.data?.dataRoot, server.data.id, 'data')}
            />
            <PathRow
              label={t('server.pathConfig')}
              value={instancePath(system.data?.dataRoot, server.data.id, 'config')}
            />
            <PathRow
              label={t('server.pathMods')}
              value={instancePath(system.data?.dataRoot, server.data.id, 'mods')}
            />
            <PathRow
              label={t('server.pathLogs')}
              value={instancePath(system.data?.dataRoot, server.data.id, 'logs')}
            />
          </Table.Tbody>
        </Table>
      </Card>
    </SimpleGrid>
  );
}

function instancePath(dataRoot: string | undefined, id: string, folder: string) {
  return dataRoot ? `${dataRoot}\\instances\\${id}\\${folder}` : '—';
}

function PathRow({ label, value }: { label: string; value: string }) {
  return (
    <Table.Tr>
      <Table.Td w={160} c="dimmed" fz="sm">
        {label}
      </Table.Td>
      <Table.Td fz="sm">
        <Text ff="monospace" size="sm" style={{ overflowWrap: 'anywhere' }}>
          {value}
        </Text>
      </Table.Td>
    </Table.Tr>
  );
}

function resources(amounts: ResourceAmounts | null) {
  if (!amounts) {
    return '—';
  }

  return (
    <Group gap={6}>
      {amounts.cpuCores != null && <Badge variant="default">{amounts.cpuCores} CPU</Badge>}
      {amounts.ramMb != null && <Badge variant="default">{amounts.ramMb} MB RAM</Badge>}
      {amounts.diskMb != null && <Badge variant="default">{amounts.diskMb} MB disk</Badge>}
    </Group>
  );
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <Table.Tr>
      <Table.Td w={140} c="dimmed" fz="sm">
        {label}
      </Table.Td>
      <Table.Td fz="sm">{value}</Table.Td>
    </Table.Tr>
  );
}
