import dayjs from 'dayjs';
import { useState } from 'react';
import { IconArrowRight, IconFileText, IconSearch } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Card, Group, Stack, Text, TextInput, Title } from '@mantine/core';
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
  const [query, setQuery] = useState('');
  const files = (configs.data?.files ?? []).filter((file) =>
    file.path.toLowerCase().includes(query.trim().toLowerCase())
  );

  return (
    <Card p={0}>
      <Group justify="space-between" align="flex-start" gap="md" p="md" pb="sm" wrap="wrap">
        <Stack gap={2}>
          <Group gap={4}>
            <Title order={5}>{t('mods.configs.title')}</Title>
            <HelpButton label={t('mods.configs.title')} help={{ topicId: 'mods.configs' }} />
          </Group>
          <Text size="sm" c="dimmed">
            {t('mods.configs.text')}
          </Text>
        </Stack>
        <TextInput
          value={query}
          onChange={(event) => setQuery(event.currentTarget.value)}
          placeholder={t('mods.configs.searchPlaceholder')}
          aria-label={t('mods.configs.search')}
          leftSection={<IconSearch size={16} aria-hidden />}
          w={{ base: '100%', sm: 240 }}
        />
      </Group>
      {configs.isError ? (
        <ProblemAlert error={configs.error} onRetry={() => void configs.refetch()} />
      ) : (
        <DataTable
          records={files}
          idAccessor="path"
          fetching={configs.isFetching && !configs.data}
          minHeight={files.length === 0 ? 120 : undefined}
          noRecordsText={
            query.trim() ? t('mods.configs.noMatches', { query }) : t('mods.configs.empty')
          }
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
            {
              accessor: 'actions',
              title: '',
              width: 48,
              textAlign: 'right',
              render: (file) => (
                <ActionIcon
                  variant="subtle"
                  aria-label={`${t('mods.configs.open')}: ${file.path}`}
                  onClick={(event) => {
                    event.stopPropagation();
                    setOpen(file.path);
                  }}
                >
                  <IconArrowRight size={16} />
                </ActionIcon>
              ),
            },
          ]}
        />
      )}
      <ConfigDrawer server={server} path={open} onClose={() => setOpen(null)} />
    </Card>
  );
}
