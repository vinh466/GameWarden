import { useState } from 'react';
import { IconBan, IconUserMinus } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Button,
  Card,
  Group,
  Modal,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core';
import type { GameIntegrationPlayer, GameIntegrationView } from '@/api/generated';
import { useIntegrationAction } from './useIntegrationAction';

type PlayerAction = 'kick' | 'ban';

/**
 * The connected players a provider lists (docs/16). Kick and ban ask for a reason first; a player without a user id
 * cannot be acted on, and the buttons say why.
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
  if (!view.capabilities.includes('players')) {
    return null;
  }

  const close = () => {
    setTarget(null);
    setReason('');
  };
  const name = (player: GameIntegrationPlayer) =>
    player.name ?? player.accountName ?? player.userId ?? player.playerId ?? '—';

  return (
    <>
      <Card p={0}>
        <Stack gap={0}>
          <Group justify="space-between" p="md" pb="sm">
            <Title order={5}>{t('integration.players.title')}</Title>
            <Text size="sm" c="dimmed">
              {view.players.length}
            </Text>
          </Group>
          {view.players.length === 0 ? (
            <Text size="sm" c="dimmed" p="md" pt={0}>
              {t('integration.players.empty')}
            </Text>
          ) : (
            <Table.ScrollContainer minWidth={620}>
              <Table verticalSpacing="xs" highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('integration.players.name')}</Table.Th>
                    <Table.Th>{t('integration.players.level')}</Table.Th>
                    <Table.Th>{t('integration.players.ping')}</Table.Th>
                    <Table.Th>{t('integration.players.ip')}</Table.Th>
                    <Table.Th>{t('integration.players.base')}</Table.Th>
                    {(canKick || canBan) && (
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
                          <Stack gap={0}>
                            <Text size="sm" fw={500}>
                              {name(player)}
                            </Text>
                            {player.accountName && player.accountName !== player.name && (
                              <Text size="xs" c="dimmed">
                                {player.accountName}
                              </Text>
                            )}
                          </Stack>
                        </Table.Td>
                        <Table.Td>{player.level ?? '—'}</Table.Td>
                        <Table.Td>
                          {player.ping == null ? '—' : `${Math.round(player.ping)} ms`}
                        </Table.Td>
                        <Table.Td>
                          <Text size="sm" ff="monospace">
                            {player.ip ?? '—'}
                          </Text>
                        </Table.Td>
                        <Table.Td>{player.buildingCount ?? '—'}</Table.Td>
                        {(canKick || canBan) && (
                          <Table.Td>
                            <Group gap={4} justify="flex-end" wrap="nowrap">
                              {canKick && (
                                <Tooltip
                                  label={
                                    hasId
                                      ? t('integration.players.kick')
                                      : t('integration.players.noUserId')
                                  }
                                >
                                  <ActionIcon
                                    variant="subtle"
                                    color="orange"
                                    aria-label={t('integration.players.kick')}
                                    disabled={!hasId}
                                    onClick={() => setTarget({ action: 'kick', player })}
                                  >
                                    <IconUserMinus size={16} />
                                  </ActionIcon>
                                </Tooltip>
                              )}
                              {canBan && (
                                <Tooltip
                                  label={
                                    hasId
                                      ? t('integration.players.ban')
                                      : t('integration.players.noUserId')
                                  }
                                >
                                  <ActionIcon
                                    variant="subtle"
                                    color="red"
                                    aria-label={t('integration.players.ban')}
                                    disabled={!hasId}
                                    onClick={() => setTarget({ action: 'ban', player })}
                                  >
                                    <IconBan size={16} />
                                  </ActionIcon>
                                </Tooltip>
                              )}
                            </Group>
                          </Table.Td>
                        )}
                      </Table.Tr>
                    );
                  })}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
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
