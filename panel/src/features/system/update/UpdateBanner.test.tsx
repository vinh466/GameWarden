import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, userEvent } from '@test-utils';
import { http, HttpResponse } from 'msw/http';
import { setupServer } from 'msw/node';
import { client } from '@/api/generated/client.gen';
import { UpdateBanner } from './UpdateBanner';
import {
  beginUpdate,
  clearUpdate,
  getUpdateState,
  markFailed,
  markRestarting,
  markSucceeded,
} from './updateStore';

const target = {
  from: '0.3.0-dev.8',
  to: '0.3.0-dev.9',
  notes: '## Data impact\n\nNothing to migrate.',
  url: 'https://github.com/vinh466/GameWarden/releases/tag/v0.3.0-dev.9',
  publishedAt: '2026-11-05T10:00:00Z',
};

const server = setupServer(
  http.get('/api/v1/system/update/status', () =>
    HttpResponse.json({
      phase: 'downloading',
      percent: 42,
      message: 'Downloading the package.',
      startedAt: new Date().toISOString(),
      finishedAt: null,
      error: null,
    })
  )
);

beforeAll(() => {
  client.setConfig({ baseUrl: window.location.origin });
  server.listen({ onUnhandledFrame: 'error' });
});
afterEach(() => {
  server.resetHandlers();
  window.sessionStorage.clear();
  clearUpdate();
});
afterAll(() => server.close());

function renderBanner() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <UpdateBanner />
    </QueryClientProvider>
  );
}

describe('UpdateBanner', () => {
  it('shows a real download step and percentage while preparing', async () => {
    beginUpdate(target);
    renderBanner();

    expect(screen.getByText(`Preparing update to ${target.to}`)).toBeInTheDocument();
    expect(screen.getByText('Downloading the package…')).toBeInTheDocument();
    expect(await screen.findByText('42%', undefined, { timeout: 5_000 })).toBeInTheDocument();
  });

  it('turns success into reload and release notes, and keeps the notes reachable', async () => {
    beginUpdate(target);
    markRestarting();
    markSucceeded();
    renderBanner();

    expect(screen.getByText(`Updated to ${target.to}`)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: "What's new" }));
    expect(getUpdateState().notesOpen).toBe(true);
    expect(screen.getByRole('button', { name: 'Reload panel' })).toBeInTheDocument();
  });

  it('keeps a failure visible and reopens the dialog from Retry', async () => {
    beginUpdate(target);
    markFailed('The package signature did not match.');
    renderBanner();

    expect(screen.getByText('The package signature did not match.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(getUpdateState().dialogOpen).toBe(true);
  });
});
