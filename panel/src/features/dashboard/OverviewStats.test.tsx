import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@test-utils';
import { http, HttpResponse } from 'msw/http';
import { setupServer } from 'msw/node';
import { client } from '@/api/generated/client.gen';
import { OverviewStats } from './OverviewStats';

const unavailable = () => HttpResponse.json({ title: 'Core is down' }, { status: 500 });
const mock = setupServer(
  http.get('/api/v1/system', unavailable),
  http.get('/api/v1/instances', unavailable),
  http.get('/api/v1/jobs', unavailable),
  http.get('/api/v1/metrics', unavailable)
);

beforeAll(() => {
  client.setConfig({ baseUrl: window.location.origin });
  mock.listen({ onUnhandledFrame: 'error' });
});
afterEach(() => mock.resetHandlers());
afterAll(() => mock.close());

describe('OverviewStats', () => {
  it('shows the API error with a retry instead of a skeleton that never ends', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <OverviewStats />
      </QueryClientProvider>
    );

    expect(await screen.findByText('Core is down')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
