import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Badge, Card, Group, SimpleGrid, Table, Text, Title } from '@mantine/core';
import type { ResourceAmounts } from '@/api/generated';
import { queries } from '@/api/queries';

export const Route = createFileRoute('/_app/servers/$id/info')({
  component: Info,
});

function Info() {
  const { t } = useTranslation();
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const adapters = useQuery(queries.adapters());
  const adapter = adapters.data?.adapters.find(
    (candidate) => candidate.id === server.data?.adapterId
  );
  if (!server.data || !adapter) {
    return null;
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
              value={adapter.ports.map((port) => `${port.name} ${port.protocol}`).join(', ')}
            />
          </Table.Tbody>
        </Table>
      </Card>
    </SimpleGrid>
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
