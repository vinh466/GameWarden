import { useMutation } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { startUpdate } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { beginUpdate, markFailed, markRestarting, type UpdateTarget } from './updateStore';

/**
 * Starts the update the dialog showed. The exact version is sent, not "latest": if a new release lands between the
 * check and the click, the panel still installs the release whose notes the Owner read.
 */
export function useStartUpdate() {
  const { t } = useTranslation();
  const mutation = useMutation({
    mutationFn: async (target: UpdateTarget) => {
      beginUpdate(target);
      const { data } = await startUpdate({ body: { version: target.to }, throwOnError: true });
      return data;
    },
    onSuccess: () => markRestarting(),
    onError: (error) => {
      markFailed(toProblem(error, t('errors.generic')).title);
    },
  });

  return { start: mutation.mutate, pending: mutation.isPending };
}
