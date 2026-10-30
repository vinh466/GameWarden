import { IconChevronDown, IconRefresh } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Button,
  Card,
  Code,
  Collapse,
  Group,
  ScrollArea,
  Stack,
  Text,
  Title,
  Tooltip,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';

/** BepInEx's own log of the set that runs (docs/13 §4.3), closed until opened, refreshed on demand. */
export function LoaderLog({ id }: { id: string }) {
  const { t } = useTranslation();
  const [opened, { toggle }] = useDisclosure(false);
  const log = useQuery({ ...queries.modLog(id), enabled: opened });
  const lines = log.data?.lines ?? [];

  return (
    <Card>
      <Group justify="space-between" wrap="nowrap">
        <Stack gap={2}>
          <Title order={5}>{t('mods.log.title')}</Title>
          <Text size="sm" c="dimmed">
            {t('mods.log.text')}
          </Text>
        </Stack>
        <Group gap="xs" wrap="nowrap">
          {opened && (
            <Tooltip label={t('mods.log.refresh')}>
              <ActionIcon
                variant="default"
                size="lg"
                aria-label={t('mods.log.refresh')}
                loading={log.isFetching}
                onClick={() => void log.refetch()}
              >
                <IconRefresh size={16} />
              </ActionIcon>
            </Tooltip>
          )}
          <Button
            variant="default"
            rightSection={<IconChevronDown size={16} aria-hidden />}
            aria-expanded={opened}
            onClick={toggle}
          >
            {opened ? t('mods.log.hide') : t('mods.log.show')}
          </Button>
        </Group>
      </Group>
      <Collapse expanded={opened}>
        <Stack mt="md">
          {log.isError ? (
            <ProblemAlert error={log.error} onRetry={() => void log.refetch()} />
          ) : log.data && lines.length === 0 ? (
            <Text size="sm" c="dimmed">
              {t('mods.log.empty')}
            </Text>
          ) : (
            <ScrollArea h={360} type="auto">
              <Code block fz="xs">
                {lines.join('\n')}
              </Code>
            </ScrollArea>
          )}
        </Stack>
      </Collapse>
    </Card>
  );
}
