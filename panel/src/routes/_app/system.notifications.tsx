import { createFileRoute } from '@tanstack/react-router';
import { NotificationsScreen } from '@/features/system/notifications/NotificationsScreen';

export const Route = createFileRoute('/_app/system/notifications')({
  component: NotificationsScreen,
});
