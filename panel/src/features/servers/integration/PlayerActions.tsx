import {
  IconBan,
  IconDotsVertical,
  IconShield,
  IconShieldOff,
  IconUserMinus,
} from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Menu } from '@mantine/core';
import type { GameIntegrationPlayer } from '@/api/generated';

/**
 * Admin, kick and ban for one player (docs/22 §7.9). The actions are words in a menu instead of three icons the
 * Owner has to decode; a player without a user id says why on the open menu.
 */
export function PlayerActions({
  player,
  hasId,
  canAdmin,
  canKick,
  canBan,
  pendingAction,
  onAdmin,
  onKick,
  onBan,
}: {
  player: GameIntegrationPlayer;
  hasId: boolean;
  canAdmin: boolean;
  canKick: boolean;
  canBan: boolean;
  pendingAction: string | null;
  onAdmin: () => void;
  onKick: () => void;
  onBan: () => void;
}) {
  const { t } = useTranslation();
  if (!canAdmin && !canKick && !canBan) {
    return null;
  }

  const label = player.name ?? player.accountName ?? player.userId ?? player.playerId ?? '';
  const disabled = !hasId || pendingAction !== null;
  return (
    <Menu position="bottom-end" withinPortal>
      <Menu.Target>
        <ActionIcon variant="subtle" aria-label={t('integration.players.actions', { name: label })}>
          <IconDotsVertical size={16} />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        {!hasId && <Menu.Label maw={260}>{t('integration.players.noUserId')}</Menu.Label>}
        {canAdmin && (
          <Menu.Item
            leftSection={player.isAdmin ? <IconShieldOff size={16} /> : <IconShield size={16} />}
            disabled={disabled}
            onClick={onAdmin}
          >
            {t(player.isAdmin ? 'integration.players.unadmin' : 'integration.players.makeAdmin')}
          </Menu.Item>
        )}
        {canKick && (
          <Menu.Item leftSection={<IconUserMinus size={16} />} disabled={disabled} onClick={onKick}>
            {t('integration.players.kick')}
          </Menu.Item>
        )}
        {canBan && (
          <Menu.Item
            color="red"
            leftSection={<IconBan size={16} />}
            disabled={disabled}
            onClick={onBan}
          >
            {t('integration.players.ban')}
          </Menu.Item>
        )}
      </Menu.Dropdown>
    </Menu>
  );
}
