import { createFileRoute } from '@tanstack/react-router';
import { AgentsScreen } from '@/features/agents/AgentsScreen';

export const Route = createFileRoute('/_app/system/agents')({
  component: AgentsScreen,
});
