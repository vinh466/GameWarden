import { createFileRoute, redirect } from '@tanstack/react-router';
import { sessionQuery, setupStatusQuery } from '@/api/session';
import { AppLayout } from '@/layout/AppLayout';

// Pathless layout: every page inside the application shell, for a signed-in user only (TanStack Router's
// "Authenticated Routes" guide). Before the Owner exists, the panel goes to first-run setup instead.
export const Route = createFileRoute('/_app')({
  beforeLoad: async ({ context: { queryClient }, location }) => {
    const session = await queryClient.query(sessionQuery()).catch(() => null);
    if (!session) {
      const setup = await queryClient.query(setupStatusQuery()).catch(() => null);
      throw redirect(
        setup?.needsSetup ? { to: '/setup' } : { to: '/login', search: { redirect: location.href } }
      );
    }

    return { session };
  },
  component: AppLayout,
});
