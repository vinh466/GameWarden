import type { ProblemDetails } from './generated';

/** A failed API call as the panel shows it: ProblemDetails with the stable `code` of docs/05 §5. */
export interface ApiProblem {
  status?: number;
  code?: string;
  title: string;
}

/** Errors thrown by the generated client are the ProblemDetails body; anything else (network, parsing) is generic. */
export function toProblem(error: unknown, fallback: string): ApiProblem {
  if (error && typeof error === 'object' && 'title' in error) {
    const problem = error as ProblemDetails & { code?: unknown };
    return {
      status: problem.status ?? undefined,
      code: typeof problem.code === 'string' ? problem.code : undefined,
      title: problem.title ?? fallback,
    };
  }

  return { title: error instanceof Error && error.message ? error.message : fallback };
}
