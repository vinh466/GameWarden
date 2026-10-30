import dayjs from 'dayjs';
import { useState } from 'react';
import { IconRestore } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Group, Skeleton, Stack, Text, Title } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import {
  restoreInstanceModsConfigRevision,
  type InstanceSnapshot,
  type ModConfigRevisionKind,
  type ModConfigSaved,
} from '@/api/generated';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { DiffView } from './DiffView';
import { fileSize } from './fileSize';
import { refreshConfig, useModsFailure } from './useModsSync';
import { Why } from './Why';

const kindColors: Record<ModConfigRevisionKind, string> = {
  server: 'gray',
  edit: 'blue',
  restore: 'grape',
};

/**
 * A config file's revisions, newest first (ADR-0021): a selected one is compared line by line with the current file and
 * can be restored, after a confirmation, as a new revision.
 */
export function ConfigHistory({
  server,
  path,
  onRestored,
}: {
  server: InstanceSnapshot;
  path: string;
  onRestored: (result: ModConfigSaved) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const failed = useModsFailure();
  const revisions = useQuery(queries.modConfigRevisions(server.id, path));
  const current = useQuery(queries.modConfig(server.id, path));
  const [selected, setSelected] = useState<number | null>(null);
  const revision = useQuery({
    ...queries.modConfigRevision(server.id, selected ?? 0),
    enabled: selected !== null,
  });
  const restore = useMutation({
    mutationFn: async ({ revisionId, base }: { revisionId: number; base: string }) =>
      (
        await restoreInstanceModsConfigRevision({
          path: { id: server.id, revisionId },
          body: { baseSha256: base },
          throwOnError: true,
        })
      ).data,
    onSuccess: (result) => {
      notifications.show({
        color: 'violet',
        message: t('mods.configs.history.restored', { path }),
      });
      setSelected(null);
      onRestored(result);
    },
    onError: (error) => failed(server.displayName, error),
    onSettled: () => refreshConfig(queryClient, server.id, path),
  });

  if (revisions.isError) {
    return <ProblemAlert error={revisions.error} onRetry={() => void revisions.refetch()} />;
  }

  const records = revisions.data?.revisions ?? [];
  const shown = selected !== null && revision.data?.id === selected ? revision.data : null;
  const confirmRestore = () => {
    if (!shown || !current.data) {
      return;
    }

    const base = current.data.sha256;
    modals.openConfirmModal({
      title: t('mods.configs.history.restoreTitle'),
      children: (
        <Text size="sm">
          {t('mods.configs.history.restoreText', {
            path,
            time: dayjs(shown.createdAt).format('YYYY-MM-DD HH:mm'),
          })}
        </Text>
      ),
      labels: { confirm: t('mods.configs.history.restoreConfirm'), cancel: t('actions.cancel') },
      onConfirm: () => restore.mutate({ revisionId: shown.id, base }),
    });
  };

  return (
    <Stack gap="sm">
      <Text size="xs" c="dimmed">
        {t('mods.configs.history.kept')}
      </Text>
      <DataTable
        records={records}
        fetching={revisions.isFetching && !revisions.data}
        minHeight={records.length === 0 ? 120 : undefined}
        noRecordsText={t('mods.configs.history.empty')}
        highlightOnHover
        onRowClick={({ record }) => setSelected(record.id)}
        rowBackgroundColor={({ id }) =>
          id === selected ? { light: 'blue.0', dark: 'dark.5' } : undefined
        }
        columns={[
          {
            accessor: 'createdAt',
            title: t('mods.configs.history.when'),
            render: (row) => (
              <Text size="sm" title={dayjs(row.createdAt).format('YYYY-MM-DD HH:mm:ss')}>
                {dayjs(row.createdAt).fromNow()}
              </Text>
            ),
          },
          {
            accessor: 'kind',
            title: t('mods.configs.history.kind'),
            render: (row) => (
              <Badge variant="light" color={kindColors[row.kind]}>
                {t(`mods.configs.history.kinds.${row.kind}`)}
              </Badge>
            ),
          },
          {
            accessor: 'size',
            title: t('mods.configs.size'),
            textAlign: 'right',
            render: (row) => fileSize(row.size),
          },
        ]}
      />
      {selected === null ? (
        records.length > 0 && (
          <Text size="sm" c="dimmed">
            {t('mods.configs.history.select')}
          </Text>
        )
      ) : shown && current.data ? (
        <Stack gap="xs">
          <Group justify="space-between" gap="xs">
            <Title order={6}>{t('mods.configs.history.compareTitle', { id: shown.id })}</Title>
            <Why
              reason={
                shown.sha256 === current.data.sha256 ? t('mods.configs.history.isCurrent') : null
              }
            >
              <Button
                size="xs"
                color="orange"
                leftSection={<IconRestore size={16} aria-hidden />}
                loading={restore.isPending}
                disabled={shown.sha256 === current.data.sha256 || !current.data.editable}
                onClick={confirmRestore}
              >
                {t('mods.configs.history.restore')}
              </Button>
            </Why>
          </Group>
          <DiffView
            oldText={shown.text}
            newText={current.data.text}
            legend={t('mods.configs.history.legend')}
          />
        </Stack>
      ) : revision.isError ? (
        <ProblemAlert error={revision.error} />
      ) : (
        <Skeleton h={160} />
      )}
    </Stack>
  );
}
