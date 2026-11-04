import { createFileRoute } from '@tanstack/react-router';
import { queries } from '@/api/queries';
import { OverviewScreen } from '@/features/system/OverviewScreen';

export const Route = createFileRoute('/_app/system/')({
  loader: ({ context: { queryClient } }) =>
    queryClient.query({ ...queries.system(), staleTime: 'static' }),
  component: OverviewScreen,
});
