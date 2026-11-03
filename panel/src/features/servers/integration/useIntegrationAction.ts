import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notifications } from '@mantine/notifications';
import { runInstanceIntegrationAction } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';

export interface IntegrationActionInput {
  action: string;
  message?: string;
  userId?: string;
}

/**
 * One mutation for every game API action (docs/16); the provider decides what the action means. The tab is read
 * again afterwards, so a kick or a save shows its effect without a manual refresh.
 */
export function useIntegrationAction(id: string, server: string) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const mutation = useMutation({
    mutationFn: async (input: IntegrationActionInput) =>
      (
        await runInstanceIntegrationAction({
          path: { id, action: input.action },
          body: {
            message: input.message ?? null,
            userId: input.userId ?? null,
            waitTime: null,
          },
          throwOnError: true,
        })
      ).data,
    onSuccess: (result) =>
      notifications.show({
        color: 'teal',
        message: t(`integration.done.${result.action}`, {
          defaultValue: t('integration.done.generic'),
        }),
      }),
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: `${server}: ${t('integration.failed')}`,
        message: toProblem(error, t('feedback.failed')).title,
      }),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: queries.integration(id).queryKey });
    },
  });

  return {
    run: mutation.mutate,
    pendingAction: mutation.isPending ? (mutation.variables?.action ?? null) : null,
  };
}
