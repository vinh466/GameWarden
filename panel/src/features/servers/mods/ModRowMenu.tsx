import {
  IconDots,
  IconPlayerPause,
  IconPlayerPlay,
  IconTrash,
  IconUpload,
} from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Menu } from '@mantine/core';
import type { ModEntryView } from '@/api/generated';

/** One row's actions: enable or disable, update to the newer version, remove. Dependencies explain why they are off. */
export function ModRowMenu({
  entry,
  dependency,
  busy,
  busyReason,
  latest,
  onToggleEnabled,
  onUpdate,
  onRemove,
}: {
  entry: ModEntryView;
  dependency: boolean;
  busy: boolean;
  busyReason: string | null;
  latest: string | undefined;
  onToggleEnabled: () => void;
  onUpdate: (version: string) => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  const off = dependency || busy;

  return (
    <Menu position="bottom-end" withinPortal>
      <Menu.Target>
        <ActionIcon
          variant="subtle"
          color="gray"
          aria-label={t('mods.rowActions', { mod: entry.package })}
        >
          <IconDots size={18} aria-hidden />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        {(dependency || busyReason) && (
          <Menu.Label maw={260}>{busyReason ?? t('mods.dependencyActions')}</Menu.Label>
        )}
        <Menu.Item
          disabled={off}
          leftSection={entry.enabled ? <IconPlayerPause size={16} /> : <IconPlayerPlay size={16} />}
          onClick={onToggleEnabled}
        >
          {entry.enabled ? t('mods.disable') : t('mods.enable')}
        </Menu.Item>
        {latest && (
          <Menu.Item
            disabled={off}
            leftSection={<IconUpload size={16} />}
            onClick={() => onUpdate(latest)}
          >
            {t('mods.updateTo', { version: latest })}
          </Menu.Item>
        )}
        <Menu.Item
          disabled={off}
          color="red"
          leftSection={<IconTrash size={16} />}
          onClick={onRemove}
        >
          {t('mods.remove')}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}
