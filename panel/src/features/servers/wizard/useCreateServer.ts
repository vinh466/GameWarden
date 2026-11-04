import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { notifications } from '@mantine/notifications';
import {
  createInstance,
  installInstance,
  updateInstanceSettings,
  type AdapterSummary,
} from '@/api/generated';
import { listInstancesQueryKey } from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';
import type { WizardForm } from './wizardForm';

/** Create → secrets → install, then follow the install job (docs/07 §6.3). */
export function useCreateServer(form: WizardForm, adapter: AdapterSummary | null) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      if (!adapter) {
        throw new Error(t('wizard.pickGame'));
      }

      const values = form.getValues();
      const { data } = await createInstance({
        body: {
          id: values.id,
          adapterId: adapter.id,
          displayName: values.displayName,
          settings: values.settings,
        },
        throwOnError: true,
      });
      // An untouched optional secret stays unset instead of storing an empty value.
      const secrets = Object.fromEntries(
        Object.entries(values.secrets).filter(([, value]) => value !== '')
      );
      if (Object.keys(secrets).length > 0) {
        await updateInstanceSettings({
          path: { id: data.id },
          body: { settings: null, secrets },
          throwOnError: true,
        });
      }

      if (adapter.sourceType === 'steamcmd') {
        await installInstance({ path: { id: data.id }, throwOnError: true });
      }

      return data;
    },
    onSuccess: async (server) => {
      await queryClient.invalidateQueries({ queryKey: listInstancesQueryKey() });
      notifications.show({
        color: 'violet',
        message: t('wizard.created', { server: server.displayName }),
      });
      await navigate({ to: '/servers/$id/jobs', params: { id: server.id } });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });
}
