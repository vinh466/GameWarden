import { toProblem } from './problems';

describe('toProblem', () => {
  it('reads the stable code and title of a ProblemDetails body (docs/05 §5)', () => {
    expect(
      toProblem({ status: 412, title: 'Below the minimum', code: 'resources_insufficient' }, 'x')
    ).toEqual({ status: 412, code: 'resources_insufficient', title: 'Below the minimum' });
  });

  it('falls back for network errors and unknown values', () => {
    expect(toProblem(new TypeError('Failed to fetch'), 'fallback').title).toBe('Failed to fetch');
    expect(toProblem(undefined, 'fallback').title).toBe('fallback');
  });
});
