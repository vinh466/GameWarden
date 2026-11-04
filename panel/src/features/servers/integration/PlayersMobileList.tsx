import { useTranslation } from 'react-i18next';
import { Badge, Divider, Group, Stack, Text } from '@mantine/core';
import type { GameIntegrationPlayer, GameIntegrationView } from '@/api/generated';
import { PlayerActions } from './PlayerActions';

/** Players as cards on phones, where the actions column of the table sits off-screen (docs/11 §6.7). */
export function PlayersMobileList({
  view,
  onAdmin,
  onKick,
  onBan,
  pendingAction,
}: {
  view: GameIntegrationView;
  onAdmin: (player: GameIntegrationPlayer) => void;
  onKick: (player: GameIntegrationPlayer) => void;
  onBan: (player: GameIntegrationPlayer) => void;
  pendingAction: string | null;
}) {
  const { t } = useTranslation();
  const name = (player: GameIntegrationPlayer) =>
    player.name ?? player.accountName ?? player.userId ?? player.playerId ?? '—';
  const canAdmin = view.capabilities.includes('admin');
  const canKick = view.capabilities.includes('kick');
  const canBan = view.capabilities.includes('ban');

  return (
    <Stack gap={0} hiddenFrom="sm">
      {view.players.map((player, index) => {
        const stats = [
          player.level != null ? `${t('integration.players.level')}: ${player.level}` : null,
          player.ping != null
            ? `${t('integration.players.ping')}: ${Math.round(player.ping)} ms`
            : null,
          player.ip ? `${t('integration.players.ip')}: ${player.ip}` : null,
          player.buildingCount != null
            ? `${t('integration.players.base')}: ${player.buildingCount}`
            : null,
        ].filter(Boolean);
        return (
          <div key={player.userId ?? player.playerId ?? `${name(player)}-${index}`}>
            <Stack gap={6} p="sm">
              <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
                <Stack gap={2} miw={0}>
                  <Group gap={6} wrap="wrap">
                    <Text size="sm" fw={500}>
                      {name(player)}
                    </Text>
                    {player.isAdmin && (
                      <Badge size="xs" color="violet" variant="light">
                        {t('integration.players.admin')}
                      </Badge>
                    )}
                    {player.online === false && (
                      <Badge size="xs" color="gray" variant="outline">
                        {t('integration.players.offline')}
                      </Badge>
                    )}
                  </Group>
                  {player.userId && player.userId !== name(player) && (
                    <Text size="xs" c="dimmed" ff="monospace" lineClamp={1}>
                      {player.userId}
                    </Text>
                  )}
                </Stack>
              </Group>
              {stats.length > 0 && (
                <Text size="xs" c="dimmed">
                  {stats.join(' · ')}
                </Text>
              )}
              <PlayerActions
                player={player}
                hasId={!!player.userId}
                canAdmin={canAdmin}
                canKick={canKick}
                canBan={canBan}
                pendingAction={pendingAction}
                onAdmin={() => onAdmin(player)}
                onKick={() => onKick(player)}
                onBan={() => onBan(player)}
              />
            </Stack>
            {index < view.players.length - 1 && <Divider />}
          </div>
        );
      })}
    </Stack>
  );
}
