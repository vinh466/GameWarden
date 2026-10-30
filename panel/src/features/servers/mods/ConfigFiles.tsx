import dayjs from 'dayjs';
import { useState } from 'react';
import { IconFileText } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { Card, Group, Stack, Text, Title } from '@mantine/core';
import type { InstanceSnapshot } from '@/api/generated';
import { queries } from '@/api/queries';
import { HelpButton } from '@/components/help/HelpButton';
import { ProblemAlert } from '@/components/ProblemAlert';
import { ConfigDrawer } from './ConfigDrawer';
import { fileSize } from './fileSize';

/**
 * The active set's config files (docs/13 §4.7), subfolders included; a row opens the file in a drawer, where it can be
 * edited and its revisions compared and restored (ADR-0021).
 */
export function ConfigFiles({ server }: { server: InstanceSnapshot }) {
  const { t } = useTranslation();
  const id = server.id;
  const configs = useQuery(queries.modConfigs(id));
  const [open, setOpen] = useState<string | null>(null);
  const files = configs.data?.files ?? [];

  return (
    <Card p={0}>
      <Stack gap={2} p="md" pb="sm">
        <Group gap={4}>
          <Title order={5}>{t('mods.configs.title')}</Title>
          <HelpButton label={t('mods.configs.title')} help={{ topicId: 'mods.configs' }} />
        </Group>
        <Text size="sm" c="dimmed">
          {t('mods.configs.text')}
        </Text>
      </Stack>
      {configs.isError ? (
        <ProblemAlert error={configs.error} onRetry={() => void configs.refetch()} />
      ) : (
        <DataTable
          records={files}
          idAccessor="path"
          fetching={configs.isFetching && !configs.data}
          minHeight={files.length === 0 ? 120 : undefined}
          noRecordsText={t('mods.configs.empty')}
          highlightOnHover
          onRowClick={({ record }) => setOpen(record.path)}
          columns={[
            {
              accessor: 'path',
              title: t('mods.configs.file'),
              render: (file) => (
                <Group gap={6} wrap="nowrap">
                  <IconFileText size={16} aria-hidden />
                  <Text size="sm" ff="monospace" truncate>
                    {file.path}
                  </Text>
                </Group>
              ),
            },
            {
              accessor: 'size',
              title: t('mods.configs.size'),
              textAlign: 'right',
              render: (file) => fileSize(file.size),
            },
            {
              accessor: 'modified',
              title: t('mods.configs.modified'),
              textAlign: 'right',
              visibleMediaQuery: (theme) => `(min-width: ${theme.breakpoints.sm})`,
              render: (file) => dayjs(file.modified).fromNow(),
            },
          ]}
        />
      )}
      <ConfigDrawer server={server} path={open} onClose={() => setOpen(null)} />
    </Card>
  );
}
