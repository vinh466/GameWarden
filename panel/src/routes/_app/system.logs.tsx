import { createFileRoute } from '@tanstack/react-router';
import { LogsScreen, logsSearchSchema } from '@/features/system/logs/LogsScreen';

export const Route = createFileRoute('/_app/system/logs')({
  validateSearch: logsSearchSchema,
  component: LogsScreen,
});
