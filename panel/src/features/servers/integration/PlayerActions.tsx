/* oxlint-disable jsx-a11y/no-noninteractive-tabindex -- a focusable span lets the tooltip explain a disabled control */
import { IconBan, IconShield, IconShieldOff, IconUserMinus } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Group, Tooltip } from '@mantine/core';
import type { GameIntegrationPlayer } from '@/api/generated';

/** Admin, kick and ban for one player; disabled actions say why in a focusable tooltip. */
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
  return (
    <Group gap={4} justify="flex-end" wrap="nowrap">
      {canAdmin && (
        <Tooltip
          label={
            hasId
              ? t(player.isAdmin ? 'integration.players.unadmin' : 'integration.players.makeAdmin')
              : t('integration.players.noUserId')
          }
        >
          <span tabIndex={0}>
            <ActionIcon
              variant="subtle"
              color="violet"
              aria-label={t(
                player.isAdmin ? 'integration.players.unadmin' : 'integration.players.makeAdmin'
              )}
              disabled={!hasId}
              loading={pendingAction === (player.isAdmin ? 'unadmin' : 'admin')}
              onClick={onAdmin}
            >
              {player.isAdmin ? <IconShieldOff size={16} /> : <IconShield size={16} />}
            </ActionIcon>
          </span>
        </Tooltip>
      )}
      {canKick && (
        <Tooltip label={hasId ? t('integration.players.kick') : t('integration.players.noUserId')}>
          <span tabIndex={0}>
            <ActionIcon
              variant="subtle"
              color="orange"
              aria-label={t('integration.players.kick')}
              disabled={!hasId}
              onClick={onKick}
            >
              <IconUserMinus size={16} />
            </ActionIcon>
          </span>
        </Tooltip>
      )}
      {canBan && (
        <Tooltip label={hasId ? t('integration.players.ban') : t('integration.players.noUserId')}>
          <span tabIndex={0}>
            <ActionIcon
              variant="subtle"
              color="red"
              aria-label={t('integration.players.ban')}
              disabled={!hasId}
              onClick={onBan}
            >
              <IconBan size={16} />
            </ActionIcon>
          </span>
        </Tooltip>
      )}
    </Group>
  );
}
