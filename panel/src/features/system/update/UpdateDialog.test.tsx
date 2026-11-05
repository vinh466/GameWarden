import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, userEvent } from '@test-utils';
import { waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw/http';
import { setupServer } from 'msw/node';
import type { JobInfo, SystemInfo, UpdateCheck } from '@/api/generated';
import { client } from '@/api/generated/client.gen';
import { queries } from '@/api/queries';
import { system as mockSystem } from '@/mocks/data';
import { UpdateDialog } from './UpdateDialog';
import {
  beginUpdate,
  clearUpdate,
  getUpdateState,
  markFailed,
  openUpdateDialog,
} from './updateStore';

vi.mock('@/components/links', () => ({
  AnchorLink: ({ to, children }: { to: string; children?: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));

const target = {
  from: '0.3.0-dev.8',
  to: '0.3.0-dev.9',
  notes: '## Data impact\n\nNothing to migrate.',
  url: 'https://github.com/vinh466/GameWarden/releases/tag/v0.3.0-dev.9',
  publishedAt: '2026-11-05T10:00:00Z',
};

const system: SystemInfo = {
  ...mockSystem,
  version: target.from,
  installRoot: 'C:\\Program Files\\GameWarden',
  dataRoot: 'C:\\GameWarden',
};

const check: UpdateCheck = {
  current: target.from,
  latest: target.to,
  available: true,
  url: target.url,
  notes: target.notes,
  publishedAt: target.publishedAt,
  checkedAt: new Date().toISOString(),
};

const runningJob: JobInfo = {
  id: 'job-1',
  type: 'update',
  instanceId: 'valheim-main',
  state: 'running',
  stage: 'steamcmd.update',
  progress: 20,
  message: null,
  createdAt: new Date().toISOString(),
  finishedAt: null,
};

const server = setupServer(
  http.get('/api/v1/system', () => HttpResponse.json(system)),
  http.get('/api/v1/jobs', () => HttpResponse.json([])),
  http.get('/api/v1/system/update', () => HttpResponse.json(check))
);

beforeAll(() => {
  client.setConfig({ baseUrl: window.location.origin });
  server.listen({ onUnhandledFrame: 'error' });
});
afterEach(() => {
  server.resetHandlers();
  clearUpdate();
});
afterAll(() => server.close());

function renderDialog(jobs: JobInfo[] = []) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false }, mutations: { retry: false } },
  });
  queryClient.setQueryData(queries.system().queryKey, system);
  queryClient.setQueryData(queries.jobs().queryKey, jobs);
  queryClient.setQueryData(queries.updateCheck().queryKey, check);
  openUpdateDialog();
  render(
    <QueryClientProvider client={queryClient}>
      <UpdateDialog />
    </QueryClientProvider>
  );
  return queryClient;
}

describe('UpdateDialog', () => {
  it('sends the version whose notes the Owner read, not "latest" again', async () => {
    let body: unknown;
    server.use(
      http.post('/api/v1/system/update', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(
          { from: target.from, to: target.to, message: `Updating to ${target.to}.` },
          { status: 202 }
        );
      })
    );
    renderDialog();

    await userEvent.click(screen.getByRole('button', { name: `Update to ${target.to}` }));

    await waitFor(() => expect(body).toEqual({ version: target.to }), { timeout: 5_000 });
    await waitFor(() => expect(getUpdateState().phase).toBe('restarting'), { timeout: 5_000 });
  });

  it('blocks the update with a visible reason while a job is running', () => {
    renderDialog([runningJob]);

    expect(screen.getByRole('button', { name: `Update to ${target.to}` })).toBeDisabled();
    expect(screen.getAllByText(/job\(s\) running/).length).toBeGreaterThan(0);
  });

  it('keeps a previous failure in the dialog', () => {
    beginUpdate(target);
    markFailed('The package signature did not match.');
    renderDialog();

    expect(screen.getByText('The package signature did not match.')).toBeInTheDocument();
  });
});
