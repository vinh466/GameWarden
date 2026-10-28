import { QueryClient } from '@tanstack/react-query';
import { createRouter } from '@tanstack/react-router';
import { routeTree } from '../routeTree.gen';

// Router + Query wiring from TanStack Router's basic-react-query-file-based example.
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5_000,
      // ProblemDetails 4xx answers will not change by retrying.
      retry: (failureCount, error) =>
        failureCount < 2 &&
        !(error && typeof error === 'object' && 'status' in error && Number(error.status) < 500),
    },
  },
});

export const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPreload: 'intent',
  // With TanStack Query the loaders always run; Query decides what is fresh.
  defaultPreloadStaleTime: 0,
  scrollRestoration: true,
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
