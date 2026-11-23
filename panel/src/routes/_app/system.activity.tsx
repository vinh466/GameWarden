import { createFileRoute, redirect } from '@tanstack/react-router';
import { z } from 'zod';
import { activityGroups } from '@/features/activity/activityKinds';

/** Activity moved out of System (ADR-0035); old bookmarks keep their server and type filters. */
export const Route = createFileRoute('/_app/system/activity')({
  validateSearch: z.object({
    server: z.string().optional(),
    type: z.enum(activityGroups).optional(),
  }),
  beforeLoad: ({ search }) => {
    throw redirect({ to: '/activity', search });
  },
});
