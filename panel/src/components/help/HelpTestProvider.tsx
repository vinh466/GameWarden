import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { HelpProvider } from './HelpProvider';

/** Tests only: the help drawer with a query cache, as App.tsx mounts it; seed the cache with `client.setQueryData`. */
export function HelpTestProvider({
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  children,
}: {
  client?: QueryClient;
  children: ReactNode;
}) {
  return (
    <QueryClientProvider client={client}>
      <HelpProvider>{children}</HelpProvider>
    </QueryClientProvider>
  );
}
