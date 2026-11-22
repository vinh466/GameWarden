import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from '@tanstack/react-router';
import { render as testingLibraryRender } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import '../src/i18n';
import { theme } from '../src/theme';

// From Mantine's Vite template: render inside the app's MantineProvider. i18n is loaded so components show English text.
export function render(ui: React.ReactNode) {
  return testingLibraryRender(ui, {
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <MantineProvider theme={theme} env="test">
        {children}
      </MantineProvider>
    ),
  });
}

/** The same renderer inside a memory router, for components with `Link`/`ButtonLink` (routes used in tests only). */
export async function renderWithRouter(ui: React.ReactNode) {
  const rootRoute = createRootRoute();
  const indexRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/',
    component: () => <>{ui}</>,
  });
  const links = ['/servers', '/system', '/system/notifications'].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path })
  );
  const router = createRouter({
    routeTree: rootRoute.addChildren([indexRoute, ...links]),
    history: createMemoryHistory({ initialEntries: ['/'] }),
    scrollRestoration: false,
  });
  await router.load();
  return render(<RouterProvider router={router} />);
}
