import { createFileRoute, redirect } from '@tanstack/react-router';

/** The Agents screen moved into System. */
export const Route = createFileRoute('/_app/agents')({
  beforeLoad: () => {
    throw redirect({ to: '/system/agents' });
  },
});
