import dayjs from 'dayjs';
import { IconArrowRight, IconFileText } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Card, Group, Stack, Text, UnstyledButton } from '@mantine/core';
import type { ModConfigFileInfo } from '@/api/generated';
import { fileSize } from './fileSize';

/** Config files as cards on phones, so the path and its copy/open action are not clipped (docs/22 §7.8). */
export function ConfigFilesMobileList({
  files,
  empty,
  onOpen,
}: {
  files: ModConfigFileInfo[];
  empty: string;
  onOpen: (path: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap="xs" p="xs" hiddenFrom="sm">
      {files.length === 0 ? (
        <Text size="sm" c="dimmed" p="xs">
          {empty}
        </Text>
      ) : (
        files.map((file) => (
          <Card key={file.path} withBorder p="sm">
            <Group gap="xs" wrap="nowrap">
              <UnstyledButton
                onClick={() => onOpen(file.path)}
                style={{ flex: 1, minWidth: 0, textAlign: 'left' }}
                aria-label={`${t('mods.configs.open')}: ${file.path}`}
              >
                <Group gap="xs" wrap="nowrap" align="flex-start">
                  <IconFileText size={16} aria-hidden />
                  <Stack gap={2} miw={0}>
                    <Text size="sm" ff="monospace" truncate title={file.path}>
                      {file.path}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {fileSize(file.size)} · {dayjs(file.modified).fromNow()}
                    </Text>
                  </Stack>
                </Group>
              </UnstyledButton>
              <ActionIcon
                variant="subtle"
                aria-label={`${t('mods.configs.open')}: ${file.path}`}
                onClick={() => onOpen(file.path)}
              >
                <IconArrowRight size={16} />
              </ActionIcon>
            </Group>
          </Card>
        ))
      )}
    </Stack>
  );
}
