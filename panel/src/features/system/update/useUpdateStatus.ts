import { useQuery } from '@tanstack/react-query';
import type { SystemInfo } from '@/api/generated';
import { queries } from '@/api/queries';

/**
 * The newest release from the panel's check, falling back to Core's daily check. The header and the System card share
 * this query, so a manual check updates both at once (docs/11 §2 System).
 */
export function useUpdateStatus(systemOverride?: SystemInfo) {
  const systemQuery = useQuery({ ...queries.system(), enabled: !systemOverride });
  const system = systemOverride ?? systemQuery.data;
  const check = useQuery({
    ...queries.updateCheck(),
    enabled: system ? !!system.installRoot : false,
    // A development release can land while the panel stays open. Two calls an hour stay far below GitHub's limit.
    refetchInterval: 30 * 60_000,
  });

  return {
    system,
    systemQuery,
    check,
    status: check.data ?? system?.updateCheck ?? null,
  };
}
