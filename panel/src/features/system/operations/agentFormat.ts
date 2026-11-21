/** One place for the Agent activity units (docs/22 §7.14). */
export function formatLatency(value?: number) {
  if (value === undefined || value === null) {
    return '—';
  }
  return value < 1000 ? `${Math.round(value)} ms` : `${(value / 1000).toFixed(1)} s`;
}

export function formatTokens(data?: { promptTokens?: number; completionTokens?: number }) {
  const total = (data?.promptTokens ?? 0) + (data?.completionTokens ?? 0);
  return total < 1000 ? String(total) : `${(total / 1000).toFixed(1)}k`;
}
