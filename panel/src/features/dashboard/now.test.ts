import { describe, expect, it } from 'vitest';
import type { AttentionItem } from './attention';
import { nowSummary } from './now';

describe('nowSummary', () => {
  it('reports the healthy counts when nothing needs attention', () => {
    expect(nowSummary([], 4, 1)).toEqual({ kind: 'healthy', servers: 4, jobs: 1 });
  });

  it('puts problems first, whatever else is true', () => {
    const items = [
      { kind: 'core', at: '2026-11-22T00:00:00Z', message: 'restarted' },
    ] as AttentionItem[];
    expect(nowSummary(items, 4, 0)).toEqual({ kind: 'problems', count: 1 });
  });
});
