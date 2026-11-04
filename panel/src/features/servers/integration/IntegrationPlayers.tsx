import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Box,
  Button,
  Card,
  Group,
  Modal,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import type { GameIntegrationPlayer, GameIntegrationView } from '@/api/generated';
import { PlayerActions } from './PlayerActions';
import { PlayersMobileList } from './PlayersMobileList';
import { useIntegrationAction } from './useIntegrationAction';

type PlayerAction = 'kick' | 'ban';

/**
 * The players a provider lists (docs/16, docs/17). Kick and ban ask for a reason first; the admin toggle writes the
 * game's own admin list (Valheim needs a restart, which the toast says). A player without a user id cannot be acted on.
 */
export function IntegrationPlayers({
  id,
  server,
  view,
}: {
  id: string;
  server: string;
  view: GameIntegrationView;
}) {
  const { t } = useTranslation();
  const { run, pendingAction } = useIntegrationAction(id, server);
  const [target, setTarget] = useState<{
    action: PlayerAction;
    player: GameIntegrationPlayer;
  } | null>(null);
  const [reason, setReason] = useState('');
  const canKick = view.capabilities.includes('kick');
  const canBan = view.capabilities.includes('ban');
  const canAdmin = view.capabilities.includes('admin');
  if (!view.capabilities.includes('players')) {
    return null;
  }

  const close = () => {
    setTarget(null);
    setReason('');
  };
  const name = (player: GameIntegrationPlayer) =>
    player.name ?? player.accountName ?? player.userId ?? player.playerId ?? '—';
  const subtitle = (player: GameIntegrationPlayer) => {
    const shown = name(player);
    if (player.accountName && player.accountName !== shown) {
      return player.accountName;
    }

    return player.userId && player.userId !== shown ? player.userId : null;
  };
  const hasLevel = view.players.some((player) => player.level != null);
  const hasPing = view.players.some((player) => player.ping != null);
  const hasIp = view.players.some((player) => player.ip != null);
  const hasBase = view.players.some((player) => player.buildingCount != null);
  const hasActions = canKick || canBan || canAdmin;
  const online = view.players.filter((player) => player.online !== false).length;

  return (
    <>
      <Card p={0}>
        <Stack gap={0}>
          <Group justify="space-between" p="md" pb="sm">
            <Title order={5}>{t('integration.players.title')}</Title>
            <Text size="sm" c="dimmed">
              {online}
            </Text>
          </Group>
          {view.players.length === 0 ? (
            <Text size="sm" c="dimmed" p="md" pt={0}>
              {t('integration.players.empty')}
            </Text>
          ) : (
            <>
              <PlayersMobileList
                view={view}
                pendingAction={pendingAction}
                onAdmin={(player) => {
                  if (player.userId) {
                    run({
                      action: player.isAdmin ? 'unadmin' : 'admin',
                      userId: player.userId,
                    });
                  }
                }}
                onKick={(player) => setTarget({ action: 'kick', player })}
                onBan={(player) => setTarget({ action: 'ban', player })}
              />
              <Box visibleFrom="sm">
                <Table.ScrollContainer minWidth={hasActions ? 420 : 320}>
                  <Table
                    verticalSpacing="xs"
                    highlightOnHover
                    aria-label={t('integration.players.title')}
                  >
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('integration.players.name')}</Table.Th>
                        {hasLevel && <Table.Th>{t('integration.players.level')}</Table.Th>}
                        {hasPing && <Table.Th>{t('integration.players.ping')}</Table.Th>}
                        {hasIp && <Table.Th>{t('integration.players.ip')}</Table.Th>}
                        {hasBase && <Table.Th>{t('integration.players.base')}</Table.Th>}
                        {hasActions && (
                          <Table.Th>{t('integration.players.actionsHeader')}</Table.Th>
                        )}
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {view.players.map((player, index) => {
                        const hasId = !!player.userId;
                        return (
                          <Table.Tr
                            key={player.userId ?? player.playerId ?? `${name(player)}-${index}`}
                          >
                            <Table.Td>
                              <Stack gap={2}>
                                <Group gap={6} wrap="nowrap">
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
                                {subtitle(player) && (
                                  <Text size="xs" c="dimmed" ff="monospace">
                                    {subtitle(player)}
                                  </Text>
                                )}
                              </Stack>
                            </Table.Td>
                            {hasLevel && <Table.Td>{player.level ?? '—'}</Table.Td>}
                            {hasPing && (
                              <Table.Td>
                                {player.ping == null ? '—' : `${Math.round(player.ping)} ms`}
                              </Table.Td>
                            )}
                            {hasIp && (
                              <Table.Td>
                                <Text size="sm" ff="monospace">
                                  {player.ip ?? '—'}
                                </Text>
                              </Table.Td>
                            )}
                            {hasBase && <Table.Td>{player.buildingCount ?? '—'}</Table.Td>}
                            {hasActions && (
                              <Table.Td>
                                <PlayerActions
                                  player={player}
                                  hasId={hasId}
                                  canAdmin={canAdmin}
                                  canKick={canKick}
                                  canBan={canBan}
                                  pendingAction={pendingAction}
                                  onAdmin={() => {
                                    if (player.userId) {
                                      run({
                                        action: player.isAdmin ? 'unadmin' : 'admin',
                                        userId: player.userId,
                                      });
                                    }
                                  }}
                                  onKick={() => setTarget({ action: 'kick', player })}
                                  onBan={() => setTarget({ action: 'ban', player })}
                                />
                              </Table.Td>
                            )}
                          </Table.Tr>
                        );
                      })}
                    </Table.Tbody>
                  </Table>
                </Table.ScrollContainer>
              </Box>
            </>
          )}
        </Stack>
      </Card>

      <Modal
        opened={!!target}
        onClose={close}
        title={
          target
            ? t(
                target.action === 'kick'
                  ? 'integration.players.kickTitle'
                  : 'integration.players.banTitle',
                { name: name(target.player) }
              )
            : ''
        }
      >
        <Stack gap="md">
          <TextInput
            label={t('integration.players.reason')}
            description={t('integration.players.reasonHint')}
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
            maxLength={512}
            data-autofocus
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={close}>
              {t('actions.cancel')}
            </Button>
            <Button
              color={target?.action === 'ban' ? 'red' : 'orange'}
              loading={!!target && pendingAction === target.action}
              onClick={() => {
                if (!target?.player.userId) {
                  return;
                }

                run(
                  {
                    action: target.action,
                    userId: target.player.userId,
                    message: reason.trim(),
                  },
                  { onSuccess: close }
                );
              }}
            >
              {t(
                target?.action === 'ban'
                  ? 'integration.players.confirmBan'
                  : 'integration.players.confirmKick'
              )}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
