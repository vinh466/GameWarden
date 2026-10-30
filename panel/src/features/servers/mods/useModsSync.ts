import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notifications } from '@mantine/notifications';
import { updateInstanceMods, type ModsUpdate } from '@/api/generated';
import { listJobsQueryKey } from '@/api/generated/@tanstack/react-query.gen';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';

/** After any mods change: the mods state, its config files and the job lists are read again. */
export function refreshMods(queryClient: QueryClient, id: string) {
  void queryClient.invalidateQueries({ queryKey: queries.mods(id).queryKey });
  void queryClient.invalidateQueries({ queryKey: queries.modConfigs(id).queryKey });
  void queryClient.invalidateQueries({ queryKey: listJobsQueryKey() });
}

/** After a config save or restore: the file, the list of files and the file's revisions are read again. */
export function refreshConfig(queryClient: QueryClient, id: string, path: string) {
  void queryClient.invalidateQueries({ queryKey: queries.modConfig(id, path).queryKey });
  void queryClient.invalidateQueries({ queryKey: queries.modConfigs(id).queryKey });
  void queryClient.invalidateQueries({ queryKey: queries.modConfigRevisions(id, path).queryKey });
}

/** A failed mods call as a toast with the API's message (docs/11 §6 rule 1). */
export function useModsFailure() {
  const { t } = useTranslation();
  return (server: string, error: unknown) =>
    notifications.show({
      color: 'red',
      title: `${server}: ${t('feedback.failed')}`,
      message: toProblem(error, t('feedback.failed')).title,
    });
}

/**
 * PUT /mods (docs/13 §4.4, §4.5): a new set from the whole list of chosen mods, or from a code preview. Core answers
 * with the `mods.sync` job; the set takes effect at the next start.
 */
export function useModsSync(id: string, server: string) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const failed = useModsFailure();
  return useMutation({
    mutationFn: async (body: ModsUpdate) =>
      (await updateInstanceMods({ path: { id }, body, throwOnError: true })).data,
    onSuccess: () =>
      notifications.show({ color: 'violet', message: t('mods.syncStarted', { server }) }),
    onError: (error) => failed(server, error),
    onSettled: () => refreshMods(queryClient, id),
  });
}
