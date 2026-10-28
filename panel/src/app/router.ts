import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { createRouter } from '@tanstack/react-router';
import { isSessionGone, sendCsrfToken, sessionKey } from '@/api/session';
import { routeTree } from '../routeTree.gen';

// Router + Query wiring from TanStack Router's basic-react-query-file-based example.
export const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError: signInAgain }),
  mutationCache: new MutationCache({ onError: signInAgain }),
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

sendCsrfToken(queryClient);

export const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPreload: 'intent',
  // With TanStack Query the loaders always run; Query decides what is fresh.
  defaultPreloadStaleTime: 0,
  scrollRestoration: true,
});

/** A 401 from any call means the session ended: forget it and go to the sign-in page, then back. */
function signInAgain(error: unknown) {
  const { pathname, href } = router.state.location;
  if (!isSessionGone(error) || pathname === '/login' || pathname === '/setup') {
    return;
  }

  queryClient.removeQueries({ queryKey: sessionKey() });
  void router.navigate({ to: '/login', search: { redirect: href } });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
