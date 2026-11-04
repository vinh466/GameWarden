import dayjs from 'dayjs';
import { useEffect, useState } from 'react';
import { IconPlus } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Divider, Group, Modal, Skeleton, Table, Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import {
  createAgentToken,
  revokeAgentToken,
  type AgentClientView,
  type AgentTokenCreated,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { TokenReveal } from './TokenReveal';

/** A client's tokens: list, create a new one and revoke an old one (docs/15). */
export function ClientTokensModal({
  client,
  onClose,
  onRefresh,
}: {
  client: AgentClientView | null;
  onClose: () => void;
  onRefresh: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [createdToken, setCreatedToken] = useState<AgentTokenCreated | null>(null);
  const tokens = useQuery({ ...queries.agentTokens(client?.id ?? ''), enabled: client !== null });
  useEffect(() => setCreatedToken(null), [client?.id]);

  const fail = (error: unknown) =>
    notifications.show({
      color: 'red',
      title: t('feedback.failed'),
      message: toProblem(error, t('errors.generic')).title,
    });

  const create = useMutation({
    mutationFn: () =>
      createAgentToken({
        path: { id: client!.id },
        body: { name: 'panel', expiresInDays: 30 },
        throwOnError: true,
      }),
    onSuccess: ({ data }) => {
      setCreatedToken(data);
      void queryClient.invalidateQueries();
      onRefresh();
    },
    onError: fail,
  });
  const revoke = useMutation({
    mutationFn: (tokenId: string) =>
      revokeAgentToken({ path: { id: client!.id, tokenId }, throwOnError: true }),
    onSuccess: () => {
      void queryClient.invalidateQueries();
      onRefresh();
    },
    onError: fail,
  });
  const confirmRevoke = (tokenId: string) => {
    modals.openConfirmModal({
      title: t('agents.revokeTitle'),
      children: <Text size="sm">{t('agents.revokeText')}</Text>,
      labels: { confirm: t('agents.revoke'), cancel: t('actions.cancel') },
      confirmProps: { color: 'red' },
      onConfirm: () => revoke.mutate(tokenId),
    });
  };

  return (
    <Modal
      opened={client !== null}
      onClose={onClose}
      title={t('agents.tokensTitle', { name: client?.name ?? '' })}
      size="lg"
    >
      {createdToken && <TokenReveal token={createdToken} />}
      {createdToken && <Divider my="md" />}
      <Group justify="space-between" mb="sm">
        <Text size="sm" c="dimmed">
          {t('agents.tokensText')}
        </Text>
        <Button
          size="xs"
          leftSection={<IconPlus size={14} />}
          onClick={() => create.mutate()}
          loading={create.isPending}
        >
          {t('agents.newToken')}
        </Button>
      </Group>
      {!tokens.data ? (
        <Skeleton h={120} />
      ) : tokens.data.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t('agents.noTokens')}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={520}>
          <Table aria-label={t('agents.tokensTitle', { name: client?.name ?? '' })}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('agents.tokenName')}</Table.Th>
                <Table.Th>{t('agents.tokenExpires')}</Table.Th>
                <Table.Th>{t('agents.lastUsed')}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {tokens.data.map((row) => (
                <Table.Tr key={row.id}>
                  <Table.Td>
                    <Text size="sm" ff="monospace">
                      {row.prefix}
                    </Text>
                  </Table.Td>
                  <Table.Td>{dayjs(row.expiresAt).format('L')}</Table.Td>
                  <Table.Td>
                    {row.lastUsedAt ? dayjs(row.lastUsedAt).fromNow() : t('agents.never')}
                  </Table.Td>
                  <Table.Td>
                    {row.revokedAt ? (
                      <Badge color="gray">{t('agents.revoked')}</Badge>
                    ) : (
                      <Button
                        size="compact-xs"
                        variant="subtle"
                        color="red"
                        loading={revoke.isPending && revoke.variables === row.id}
                        onClick={() => confirmRevoke(row.id)}
                      >
                        {t('agents.revoke')}
                      </Button>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Modal>
  );
}
