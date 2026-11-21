import { IconX } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Button, Group, Paper, Text } from '@mantine/core';

/** The bulk bar above the mod table: enable, disable, update or remove the selected mods as one new set. */
export function ModSelectionBar({
  count,
  busy,
  hasUpdates,
  onEnable,
  onDisable,
  onUpdate,
  onRemove,
  onClear,
}: {
  count: number;
  busy: boolean;
  hasUpdates: boolean;
  onEnable: () => void;
  onDisable: () => void;
  onUpdate: () => void;
  onRemove: () => void;
  onClear: () => void;
}) {
  const { t } = useTranslation();

  return (
    <Paper p="xs" bg="var(--mantine-color-blue-light)" radius={0}>
      <Group justify="space-between" gap="xs" wrap="wrap">
        <Text size="sm" fw={500}>
          {t('mods.selection.count', { count })}
        </Text>
        <Group gap="xs" wrap="wrap">
          <Button size="xs" variant="light" disabled={busy} onClick={onEnable}>
            {t('mods.selection.enable')}
          </Button>
          <Button size="xs" variant="light" disabled={busy} onClick={onDisable}>
            {t('mods.selection.disable')}
          </Button>
          {hasUpdates && (
            <Button size="xs" variant="light" color="violet" disabled={busy} onClick={onUpdate}>
              {t('mods.selection.update')}
            </Button>
          )}
          <Button size="xs" variant="light" color="red" disabled={busy} onClick={onRemove}>
            {t('mods.selection.remove')}
          </Button>
          <Button
            size="xs"
            variant="subtle"
            color="gray"
            leftSection={<IconX size={14} />}
            onClick={onClear}
          >
            {t('mods.selection.clear')}
          </Button>
        </Group>
      </Group>
    </Paper>
  );
}
