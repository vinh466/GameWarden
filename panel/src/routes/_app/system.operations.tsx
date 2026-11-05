import { createFileRoute } from '@tanstack/react-router';
import { OperationsScreen } from '@/features/system/operations/OperationsScreen';

export const Route = createFileRoute('/_app/system/operations')({
  component: OperationsScreen,
});
