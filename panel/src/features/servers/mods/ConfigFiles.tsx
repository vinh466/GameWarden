import dayjs from 'dayjs';
import { useState } from 'react';
import { IconFileText } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { DataTable } from 'mantine-datatable';
import { useTranslation } from 'react-i18next';
import { Card, Code, Drawer, Group, ScrollArea, Skeleton, Stack, Text, Title } from '@mantine/core';
import { queries } from '@/api/queries';
import { HelpButton } from '@/components/help/HelpButton';
import { useHelpHost } from '@/components/help/useHelp';
import { ProblemAlert } from '@/components/ProblemAlert';

const size = (bytes: number) =>
  bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(bytes < 10_240 ? 1 : 0)} KB`;

/** The active set's config files (docs/13 §4.7), subfolders included; a row opens the file, read-only. */
export function ConfigFiles({ id }: { id: string }) {
  const { t } = useTranslation();
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
              render: (file) => size(file.size),
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
      <ConfigViewer id={id} path={open} onClose={() => setOpen(null)} />
    </Card>
  );
}

/** One config file as text, monospace and read-only; editing comes later (docs/13 §2 answer 4). */
function ConfigViewer({
  id,
  path,
  onClose,
}: {
  id: string;
  path: string | null;
  onClose: () => void;
}) {
  const content = useQuery({ ...queries.modConfig(id, path ?? ''), enabled: !!path });
  const helpHost = useHelpHost();
  return (
    <Drawer opened={!!path} onClose={onClose} position="right" size="xl" title={path} {...helpHost}>
      {content.isError ? (
        <ProblemAlert error={content.error} />
      ) : content.data ? (
        <ScrollArea h="calc(100dvh - 120px)" type="auto">
          <Code block fz="xs">
            {content.data.text}
          </Code>
        </ScrollArea>
      ) : (
        <Skeleton h={240} />
      )}
    </Drawer>
  );
}
