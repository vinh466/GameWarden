import { createFileRoute, redirect } from '@tanstack/react-router';
import { z } from 'zod';
import { activityGroups } from '@/features/activity/activityKinds';

/** The Activity screen moved into System; old bookmarks keep their server and type filters. */
export const Route = createFileRoute('/_app/activity')({
  validateSearch: z.object({
    server: z.string().optional(),
    type: z.enum(activityGroups).optional(),
  }),
  beforeLoad: ({ search }) => {
    throw redirect({ to: '/system/activity', search });
  },
});
