import { createFileRoute } from '@tanstack/react-router';
import { LogsScreen } from '@/features/system/logs/LogsScreen';

export const Route = createFileRoute('/_app/system/logs')({
  component: LogsScreen,
});
