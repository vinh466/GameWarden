import { IconAlertTriangle, IconRefresh, IconStethoscope } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Card, Group, ScrollArea, Stack, Table, Text, Title } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { diagnoseAgentHostIncident } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';

const stateColor: Record<string, string> = {
  open: 'orange',
  acknowledged: 'blue',
  diagnosing: 'cyan',
  mitigating: 'violet',
  verifying: 'indigo',
  resolved: 'green',
  closed: 'gray',
  'false-alarm': 'gray',
};

/** Recent incidents and their timeline (docs/21). The Discord card is the action surface; this is the audit view. */
export function IncidentsCard() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const incidents = useQuery(queries.agentIncidents(10));
  const diagnose = useMutation({
    mutationFn: (id: string) => diagnoseAgentHostIncident({ path: { id }, throwOnError: true }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queries.agentIncidents().queryKey });
      notifications.show({ color: 'blue', message: t('system.operations.incidentDiagnosing') });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Group gap="xs">
          <IconAlertTriangle size={20} />
          <Title order={5}>{t('system.operations.incidents')}</Title>
        </Group>
        <Button
          variant="subtle"
          size="xs"
          leftSection={<IconRefresh size={14} />}
          loading={incidents.isFetching}
          onClick={() => void incidents.refetch()}
        >
          {t('actions.refresh')}
        </Button>
      </Group>
      {incidents.data?.length ? (
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="xs" highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('system.operations.incidentOpened')}</Table.Th>
                <Table.Th>{t('system.operations.incidentServer')}</Table.Th>
                <Table.Th>{t('system.operations.incidentState')}</Table.Th>
                <Table.Th>{t('system.operations.incidentTitle')}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {incidents.data.map((incident, index) => {
                const id = incident.id ?? `incident-${index}`;
                return (
                  <Table.Tr key={id}>
                    <Table.Td>
                      <Text size="xs">
                        {incident.openedAt ? new Date(incident.openedAt).toLocaleString() : ''}
                      </Text>
                    </Table.Td>
                    <Table.Td>{incident.instanceId ?? 'system'}</Table.Td>
                    <Table.Td>
                      <Badge color={stateColor[incident.state ?? ''] ?? 'gray'} variant="light">
                        {incident.state ?? ''}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" lineClamp={2}>
                        {incident.title ?? ''}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4} justify="flex-end" wrap="nowrap">
                        <Button
                          size="xs"
                          variant="light"
                          leftSection={<IconStethoscope size={14} />}
                          loading={diagnose.isPending && diagnose.variables === id}
                          onClick={() => diagnose.mutate(id)}
                        >
                          {t('system.operations.incidentDiagnose')}
                        </Button>
                        <Button
                          size="xs"
                          variant="subtle"
                          onClick={() => showIncident(id, incident.title ?? id)}
                        >
                          {t('system.operations.incidentDetails')}
                        </Button>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      ) : (
        <Text size="sm" c="dimmed">
          {t('system.operations.incidentsEmpty')}
        </Text>
      )}
    </Card>
  );
}

function showIncident(id: string, title: string) {
  modals.open({
    title,
    size: 'lg',
    children: <IncidentDetail id={id} />,
  });
}

function IncidentDetail({ id }: { id: string }) {
  const { t } = useTranslation();
  const detail = useQuery(queries.agentIncident(id));
  if (detail.isLoading) {
    return <Text size="sm">{t('system.operations.loading')}</Text>;
  }

  if (!detail.data) {
    return <Text size="sm">{t('system.operations.incidentsEmpty')}</Text>;
  }

  const { incident, timeline } = detail.data;
  return (
    <Stack gap="sm">
      <Group gap="xs">
        <Badge color={stateColor[incident.state ?? ''] ?? 'gray'}>{incident.state ?? ''}</Badge>
        <Text size="sm">{incident.instanceId ?? 'system'}</Text>
      </Group>
      {incident.summary && <Text size="sm">{incident.summary}</Text>}
      <ScrollArea h={320}>
        <Stack gap={6}>
          {timeline.map((event, index) => (
            <Stack key={event.id ?? index} gap={0}>
              <Text size="xs" c="dimmed">
                {event.at ? new Date(event.at).toLocaleString() : ''} · {event.type ?? ''}
              </Text>
              <Text size="sm">{event.text ?? ''}</Text>
            </Stack>
          ))}
        </Stack>
      </ScrollArea>
    </Stack>
  );
}
