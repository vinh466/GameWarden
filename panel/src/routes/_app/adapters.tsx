import { createFileRoute, redirect } from '@tanstack/react-router';

/** The Adapters screen moved into System. */
export const Route = createFileRoute('/_app/adapters')({
  beforeLoad: () => {
    throw redirect({ to: '/system/adapters' });
  },
});
