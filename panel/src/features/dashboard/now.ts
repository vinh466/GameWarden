import type { AttentionItem } from './attention';

export type NowSummary =
  | { kind: 'problems'; count: number }
  | { kind: 'healthy'; servers: number; jobs: number };

/** What the dashboard's top line says: problems first, otherwise the healthy counts. */
export function nowSummary(items: AttentionItem[], servers: number, jobs: number): NowSummary {
  return items.length > 0
    ? { kind: 'problems', count: items.length }
    : { kind: 'healthy', servers, jobs };
}
