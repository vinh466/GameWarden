import { createFileRoute } from '@tanstack/react-router';
import { queries } from '@/api/queries';
import { AdaptersScreen } from '@/features/adapters/AdaptersScreen';

export const Route = createFileRoute('/_app/system/adapters')({
  loader: ({ context: { queryClient } }) =>
    queryClient.query({ ...queries.adapters(), staleTime: 'static' }),
  component: AdaptersScreen,
});
