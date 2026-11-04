import { IconAlertTriangle, IconPuzzle, IconRefresh } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { Alert, Badge, Button, Card, List, Stack, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { reloadAdapters } from '@/api/generated';
import { listAdaptersQueryKey } from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';

/** The adapters Core loaded, their support level and validation problems; reload after changing one. */
export function AdaptersScreen() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const adapters = useQuery(queries.adapters());
  const reload = useMutation({
    mutationFn: () => reloadAdapters({ throwOnError: true }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: listAdaptersQueryKey() });
      notifications.show({ color: 'violet', message: t('adapters.reloaded') });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const records = adapters.data?.adapters ?? [];
  return (
    <SystemTab
      description={t('adapters.description')}
      actions={
        <>
          <Badge variant="light">{t('adapters.count', { count: records.length })}</Badge>
          <Button
            variant="default"
            leftSection={<IconRefresh size={16} />}
            loading={reload.isPending}
            onClick={() => reload.mutate()}
          >
            {t('actions.reload')}
          </Button>
        </>
      }
    >
      <Stack gap="lg">
        {adapters.data && adapters.data.problems.length > 0 && (
          <Alert
            color="red"
            icon={<IconAlertTriangle aria-hidden />}
            title={t('adapters.problems')}
          >
            <List size="sm">
              {adapters.data.problems.map((problem) => (
                <List.Item key={problem.folder}>
                  <Text size="sm" ff="monospace" span>
                    {problem.folder}
                  </Text>
                  : {problem.errors.join('; ')}
                </List.Item>
              ))}
            </List>
          </Alert>
        )}
        {adapters.isError ? (
          <ProblemAlert error={adapters.error} onRetry={() => void adapters.refetch()} />
        ) : (
          <Card p={0}>
            <DataTable
              records={records}
              fetching={adapters.isFetching}
              idAccessor="id"
              minHeight={records.length === 0 ? 220 : undefined}
              emptyState={
                <EmptyState
                  icon={IconPuzzle}
                  title={t('adapters.empty')}
                  text={t('adapters.emptyText')}
                />
              }
              columns={[
                {
                  accessor: 'name',
                  title: t('server.adapter'),
                  render: (adapter) => (
                    <Stack gap={0}>
                      <Text fw={500} size="sm">
                        {adapter.name}
                      </Text>
                      <Text size="xs" c="dimmed" ff="monospace">
                        {adapter.id}
                      </Text>
                    </Stack>
                  ),
                },
                {
                  accessor: 'level',
                  title: t('adapters.level'),
                  render: (adapter) => <Badge variant="outline">{adapter.level}</Badge>,
                },
                { accessor: 'version', title: t('adapters.version') },
                {
                  accessor: 'sourceType',
                  title: t('adapters.source'),
                  visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
                },
                {
                  accessor: 'isBuiltIn',
                  title: t('adapters.origin'),
                  render: (adapter) => (
                    <Badge color={adapter.isBuiltIn ? 'gray' : 'violet'}>
                      {adapter.isBuiltIn ? t('adapters.builtIn') : t('adapters.owner')}
                    </Badge>
                  ),
                },
              ]}
            />
          </Card>
        )}
      </Stack>
    </SystemTab>
  );
}
