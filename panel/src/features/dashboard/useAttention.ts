import { useQuery } from '@tanstack/react-query';
import { queries } from '@/api/queries';
import { attentionItems } from './attention';

/** What needs a look, for the dashboard card; null while loading. */
export function useAttention() {
  const instances = useQuery(queries.instances());
  const jobs = useQuery(queries.jobs());
  const system = useQuery(queries.system());
  const activity = useQuery(queries.activity());
  return instances.data && jobs.data && system.data
    ? attentionItems(instances.data, jobs.data, system.data, Date.now(), activity.data ?? [])
    : null;
}
