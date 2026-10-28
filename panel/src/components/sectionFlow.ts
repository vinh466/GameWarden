import { useReducer } from 'react';

/**
 * Where a guided form stands (docs/11 §4 New server): one section is open at a time, and a section opens only once
 * every section before it was completed, so going back loses nothing.
 */
export interface SectionFlowState {
  /** Index of the open section. */
  active: number;
  /** Sections up to this index can be opened; it grows as each one is completed. */
  reached: number;
}

export type SectionFlowAction =
  | { type: 'next'; count: number }
  | { type: 'back' }
  | { type: 'open'; index: number }
  | { type: 'rewind'; index: number };

export function sectionFlow(state: SectionFlowState, action: SectionFlowAction): SectionFlowState {
  switch (action.type) {
    case 'next': {
      const active = Math.min(state.active + 1, action.count - 1);
      return { active, reached: Math.max(state.reached, active) };
    }
    case 'back':
      return { ...state, active: Math.max(state.active - 1, 0) };
    case 'open':
      return action.index >= 0 && action.index <= state.reached
        ? { ...state, active: action.index }
        : state;
    case 'rewind':
      // A change that makes later sections stale (another game has other settings): they are completed again.
      return {
        active: Math.min(state.active, action.index),
        reached: Math.min(state.reached, action.index),
      };
  }
}

export type SectionStatus = 'current' | 'done' | 'next' | 'locked';

/** How a section's header looks in a guided form. */
export function sectionStatus(state: SectionFlowState, index: number): SectionStatus {
  if (index === state.active) {
    return 'current';
  }

  if (index < state.reached) {
    return 'done';
  }

  return index === state.reached ? 'next' : 'locked';
}

/** The guided flow over `count` sections; the caller validates the open section before `next`. */
export function useSectionFlow(count: number) {
  const [state, dispatch] = useReducer(sectionFlow, { active: 0, reached: 0 });
  return {
    ...state,
    isFirst: state.active === 0,
    isLast: state.active === count - 1,
    status: (index: number) => sectionStatus(state, index),
    next: () => dispatch({ type: 'next', count }),
    back: () => dispatch({ type: 'back' }),
    open: (index: number) => dispatch({ type: 'open', index }),
    rewind: (index: number) => dispatch({ type: 'rewind', index }),
  };
}

export type SectionFlow = ReturnType<typeof useSectionFlow>;
