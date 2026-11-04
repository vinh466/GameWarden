import { createFileRoute, redirect } from '@tanstack/react-router';
import { z } from 'zod';

/** The Jobs screen moved into System; old bookmarks keep their server filter. */
export const Route = createFileRoute('/_app/jobs')({
  validateSearch: z.object({ server: z.string().optional() }),
  beforeLoad: ({ search }) => {
    throw redirect({ to: '/system/jobs', search });
  },
});
