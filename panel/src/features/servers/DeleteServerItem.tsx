import { useState } from 'react';
import { IconTrash } from '@tabler/icons-react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Button, Group, Menu, Text, TextInput } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { deleteInstance, type InstanceSnapshot } from '@/api/generated';
import { listInstancesQueryKey } from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';

/** The menu item that deletes a server with its files, after the id is typed (docs/07 §1). */
export function DeleteServerItem({
  server,
  disabled,
}: {
  server: InstanceSnapshot;
  disabled: boolean;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return (
    <Menu.Item
      color="red"
      leftSection={<IconTrash size={16} />}
      disabled={disabled}
      onClick={() =>
        modals.open({
          title: t('server.deleteTitle', { server: server.displayName }),
          children: (
            <ConfirmDelete
              id={server.id}
              onConfirm={async () => {
                try {
                  await deleteInstance({
                    path: { id: server.id },
                    query: { mode: 'purge', confirm: server.id },
                    throwOnError: true,
                  });
                  modals.closeAll();
                  notifications.show({
                    color: 'violet',
                    message: t('server.deleted', { server: server.displayName }),
                  });
                  await queryClient.invalidateQueries({ queryKey: listInstancesQueryKey() });
                  await navigate({ to: '/servers' });
                } catch (error) {
                  notifications.show({
                    color: 'red',
                    title: t('feedback.failed'),
                    message: toProblem(error, t('errors.generic')).title,
                  });
                }
              }}
            />
          ),
        })
      }
    >
      {t('actions.delete')}
    </Menu.Item>
  );
}

function ConfirmDelete({ id, onConfirm }: { id: string; onConfirm: () => Promise<void> }) {
  const { t } = useTranslation();
  const [typed, setTyped] = useState('');
  const [working, setWorking] = useState(false);
  return (
    <>
      <Text size="sm" mb="md">
        {t('server.deleteText')}
      </Text>
      <TextInput
        label={t('server.deleteConfirm', { id })}
        value={typed}
        onChange={(event) => setTyped(event.currentTarget.value)}
        data-autofocus
        ff="monospace"
      />
      <Group justify="flex-end" mt="lg">
        <Button variant="default" onClick={() => modals.closeAll()}>
          {t('actions.cancel')}
        </Button>
        <Button
          color="red"
          disabled={typed !== id}
          loading={working}
          onClick={async () => {
            setWorking(true);
            await onConfirm();
            setWorking(false);
          }}
        >
          {t('actions.delete')}
        </Button>
      </Group>
    </>
  );
}
