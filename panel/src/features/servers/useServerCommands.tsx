import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { List, Stack, Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import {
  installInstance,
  restartInstance,
  startInstance,
  stopInstance,
  updateInstanceGame,
  type InstanceSnapshot,
} from '@/api/generated';
import {
  getInstanceQueryKey,
  listInstancesQueryKey,
  listJobsQueryKey,
} from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';

export type ServerCommand = 'start' | 'stop' | 'restart' | 'install' | 'update';

interface CommandInput {
  server: InstanceSnapshot;
  command: ServerCommand;
  force?: boolean;
}

/**
 * Start, stop, restart, install and update with the feedback of docs/11 §6: the result as a toast, the new state in
 * the cache, and below the minimum resources a dialog where the Owner may start anyway.
 */
export function useServerCommands() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async ({ server, command, force }: CommandInput) => {
      const path = { id: server.id };
      switch (command) {
        case 'start':
          return (await startInstance({ path, query: { force }, throwOnError: true })).data.message;
        case 'stop':
          return (await stopInstance({ path, throwOnError: true })).data.message;
        case 'restart':
          return (await restartInstance({ path, query: { force }, throwOnError: true })).data
            .message;
        case 'install':
          return (
            (await installInstance({ path, query: { force }, throwOnError: true })).data.job
              .stage ?? ''
          );
        case 'update':
          return (await updateInstanceGame({ path, throwOnError: true })).data.job.stage ?? '';
      }
    },
    onSuccess: (message, { server, command }) => {
      notifications.show({
        color: 'violet',
        message:
          command === 'install' || command === 'update'
            ? t('feedback.jobStarted', {
                type: t(`jobs.type.${command === 'install' ? 'install' : 'update'}`),
                server: server.displayName,
              })
            : t('feedback.accepted', { server: server.displayName, message }),
      });
    },
    onError: (error, input) => {
      const problem = toProblem(error, t('feedback.failed'));
      if (
        problem.code === 'resources_insufficient' &&
        (input.command === 'start' || input.command === 'restart')
      ) {
        modals.openConfirmModal({
          title: t('feedback.belowMinimum'),
          children: (
            <Stack gap="xs">
              <Text size="sm">{t('feedback.belowMinimumText')}</Text>
              <List size="sm">
                <List.Item>{problem.title}</List.Item>
              </List>
              <Text size="sm" c="dimmed">
                {t('feedback.belowMinimumHint')}
              </Text>
            </Stack>
          ),
          labels: { confirm: t('actions.startAnyway'), cancel: t('actions.cancel') },
          onConfirm: () => mutation.mutate({ ...input, force: true }),
        });
        return;
      }

      notifications.show({
        color: 'red',
        title: `${input.server.displayName}: ${t('feedback.failed')}`,
        message: problem.title,
      });
    },
    onSettled: (_data, _error, { server }) => {
      void queryClient.invalidateQueries({
        queryKey: getInstanceQueryKey({ path: { id: server.id } }),
      });
      void queryClient.invalidateQueries({ queryKey: listInstancesQueryKey() });
      void queryClient.invalidateQueries({ queryKey: listJobsQueryKey() });
    },
  });

  return {
    run: (server: InstanceSnapshot, command: ServerCommand) => mutation.mutate({ server, command }),
    /** The command in flight for this server, to show its button as pending. */
    pending: (server: InstanceSnapshot) =>
      mutation.isPending && mutation.variables.server.id === server.id
        ? mutation.variables.command
        : null,
  };
}
