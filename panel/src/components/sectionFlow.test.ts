import { sectionFlow, sectionStatus, type SectionFlowState } from './sectionFlow';

const start: SectionFlowState = { active: 0, reached: 0 };

describe('section flow', () => {
  it('opens the next section and remembers how far the Owner got', () => {
    const second = sectionFlow(start, { type: 'next', count: 4 });
    expect(second).toEqual({ active: 1, reached: 1 });
    expect(sectionFlow(second, { type: 'back' })).toEqual({ active: 0, reached: 1 });
  });

  it('stops at the last and the first section', () => {
    expect(sectionFlow({ active: 3, reached: 3 }, { type: 'next', count: 4 })).toEqual({
      active: 3,
      reached: 3,
    });
    expect(sectionFlow(start, { type: 'back' })).toEqual(start);
  });

  it('opens only sections already reached', () => {
    const state = { active: 0, reached: 2 };
    expect(sectionFlow(state, { type: 'open', index: 2 })).toEqual({ active: 2, reached: 2 });
    expect(sectionFlow(state, { type: 'open', index: 3 })).toBe(state);
  });

  it('rewinds when later sections are stale', () => {
    expect(sectionFlow({ active: 0, reached: 3 }, { type: 'rewind', index: 2 })).toEqual({
      active: 0,
      reached: 2,
    });
  });

  it('marks completed, current, next and locked sections', () => {
    const state = { active: 0, reached: 2 };
    expect([0, 1, 2, 3].map((index) => sectionStatus(state, index))).toEqual([
      'current',
      'done',
      'next',
      'locked',
    ]);
  });
});
