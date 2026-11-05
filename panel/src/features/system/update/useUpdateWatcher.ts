import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { live } from '@/api/live';
import { queries } from '@/api/queries';
import { isUpdateActive, markFailed, markSucceeded, useUpdateState } from './updateStore';

/**
 * Watches the update the panel started: the new version means success, and a fresh failed Setup result means failure.
 * The system query polls once a second while an update runs, so a restart is noticed even when the realtime link is
 * still reconnecting (docs/10 §6, docs/11 §6.1).
 */
export function useUpdateWatcher() {
  const queryClient = useQueryClient();
  const state = useUpdateState();
  const active = isUpdateActive(state);
  const system = useQuery({
    ...queries.system(),
    retry: false,
    refetchInterval: (query) =>
      active ? (query.state.status === 'error' ? 3_000 : 1_000) : live.slow,
  });

  useEffect(() => {
    if (!system.data || !state.target || !isUpdateActive(state)) {
      return;
    }

    if (system.data.version === state.target.to) {
      markSucceeded();
      void queryClient.invalidateQueries();
      return;
    }

    const last = system.data.lastUpdate;
    if (!last || last.to !== state.target.to) {
      return;
    }

    const at = Date.parse(last.at);
    const recent = Number.isFinite(at) && (!state.startedAt || at >= state.startedAt - 5_000);
    if (recent && last.outcome !== 'succeeded') {
      markFailed(last.message);
    }
  }, [system.data, state, queryClient]);
}
