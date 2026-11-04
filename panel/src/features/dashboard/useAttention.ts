import { useQuery } from '@tanstack/react-query';
import { queries } from '@/api/queries';
import { attentionItems } from './attention';

/** What needs a look, for the dashboard card; an error is reported instead of an endless skeleton. */
export function useAttention() {
  const instances = useQuery(queries.instances());
  const jobs = useQuery(queries.jobs());
  const system = useQuery(queries.system());
  const activity = useQuery(queries.activity());
  const failed = [instances, jobs, system].find((query) => query.isError);
  return {
    items:
      instances.data && jobs.data && system.data
        ? attentionItems(instances.data, jobs.data, system.data, Date.now(), activity.data ?? [])
        : null,
    error: failed?.error ?? null,
    retry: () => {
      void instances.refetch();
      void jobs.refetch();
      void system.refetch();
    },
  };
}
