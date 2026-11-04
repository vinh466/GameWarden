import dayjs from 'dayjs';
import { IconChevronRight, IconFileSearch } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Divider,
  Group,
  Stack,
  Table,
  Text,
  UnstyledButton,
  VisuallyHidden,
} from '@mantine/core';
import type { SystemLogItem } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';
import { logLevelColors } from './logs';

/** The log rows; a row opens the full record. Phones get a readable list, wider screens the table. */
export function LogTable({
  rows,
  hasFilters,
  onSelect,
  onClearFilters,
}: {
  rows: SystemLogItem[];
  hasFilters: boolean;
  onSelect: (item: SystemLogItem) => void;
  onClearFilters: () => void;
}) {
  const { t } = useTranslation();
  if (rows.length === 0) {
    return (
      <EmptyState
        icon={IconFileSearch}
        title={t('system.logs.empty')}
        text={hasFilters ? t('system.logs.emptyFilters') : undefined}
        action={
          hasFilters ? (
            <Button variant="default" onClick={onClearFilters}>
              {t('system.logs.clearFilters')}
            </Button>
          ) : undefined
        }
      />
    );
  }

  return (
    <>
      <Stack gap={0} hiddenFrom="sm">
        {rows.map((row, index) => (
          <Box key={row.id}>
            <UnstyledButton w="100%" p="sm" onClick={() => onSelect(row)}>
              <Group justify="space-between" gap="xs" wrap="nowrap">
                <Group gap="xs" wrap="nowrap">
                  <Badge color={logLevelColors[row.level] ?? 'gray'} variant="light">
                    {row.level}
                  </Badge>
                  <Text size="xs" ff="monospace" c="dimmed">
                    {dayjs(row.at).format('HH:mm:ss')} · {dayjs(row.at).format('YYYY-MM-DD')}
                  </Text>
                </Group>
                <IconChevronRight size={16} />
              </Group>
              <Text size="sm" mt={4} lineClamp={2}>
                {row.message}
              </Text>
              <Text size="xs" c="dimmed" mt={2} lineClamp={1}>
                {row.component}
              </Text>
              {(row.instanceId || row.jobId) && (
                <Group gap={4} mt={4}>
                  {row.instanceId && <Badge variant="outline">{row.instanceId}</Badge>}
                  {row.jobId && <Badge variant="outline">{row.jobId}</Badge>}
                </Group>
              )}
            </UnstyledButton>
            {index < rows.length - 1 && <Divider />}
          </Box>
        ))}
      </Stack>

      <Box visibleFrom="sm">
        <Table.ScrollContainer minWidth={960}>
          <Table highlightOnHover striped aria-label={t('system.logs.title')}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('system.logs.time')}</Table.Th>
                <Table.Th w={100}>{t('system.logs.level')}</Table.Th>
                <Table.Th>{t('system.logs.component')}</Table.Th>
                <Table.Th>{t('system.logs.message')}</Table.Th>
                <Table.Th>{t('system.logs.context')}</Table.Th>
                <Table.Th>
                  <VisuallyHidden>{t('system.logs.details')}</VisuallyHidden>
                </Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((row) => (
                <Table.Tr key={row.id} onClick={() => onSelect(row)} style={{ cursor: 'pointer' }}>
                  <Table.Td>
                    <Text size="xs" ff="monospace" title={dayjs(row.at).format('LLL')}>
                      {dayjs(row.at).format('HH:mm:ss.SSS')}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {dayjs(row.at).format('YYYY-MM-DD')}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Badge color={logLevelColors[row.level] ?? 'gray'} variant="light">
                      {row.level}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" lineClamp={1} maw={220} title={row.component}>
                      {row.component}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" lineClamp={2} title={row.message}>
                      {row.message}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {row.instanceId && <Badge variant="outline">{row.instanceId}</Badge>}
                      {row.jobId && <Badge variant="outline">{row.jobId}</Badge>}
                      {row.requestId && (
                        <Badge variant="outline" title={row.requestId}>
                          {t('system.logs.request')}
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <ActionIcon
                      variant="subtle"
                      aria-label={t('system.logs.view')}
                      onClick={(event) => {
                        event.stopPropagation();
                        onSelect(row);
                      }}
                    >
                      <IconChevronRight size={16} />
                    </ActionIcon>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Box>
    </>
  );
}
