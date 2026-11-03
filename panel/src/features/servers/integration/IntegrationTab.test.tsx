import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw/http';
import { setupServer } from 'msw/node';
import { Notifications } from '@mantine/notifications';
import type { GameIntegrationView } from '@/api/generated';
import { client } from '@/api/generated/client.gen';
import { queries } from '@/api/queries';
import { IntegrationActions } from './IntegrationActions';
import { IntegrationPlayers } from './IntegrationPlayers';
import { IntegrationSummary } from './IntegrationSummary';

const id = 'palworld-main';
const server = 'Palworld';
const view: GameIntegrationView = {
  provider: 'palworld-rest',
  label: { en: 'Palworld API' },
  capabilities: ['overview', 'players', 'announce', 'save', 'kick', 'ban', 'unban'],
  online: true,
  info: {
    version: '1.0.4',
    serverName: 'FakeGame',
    description: 'A test world',
    worldGuid: 'guid-1',
  },
  metrics: {
    serverFps: 60,
    currentPlayerNum: 1,
    serverFrameTime: 16.6,
    maxPlayerNum: 32,
    uptime: 3600,
    baseCampNum: 2,
    days: 12,
  },
  players: [
    {
      name: 'Vinh',
      accountName: 'vinh',
      playerId: 'player-1',
      userId: 'steam_1',
      ip: '127.0.0.1',
      ping: 12.5,
      x: 1.5,
      y: -2.5,
      level: 20,
      buildingCount: 3,
    },
  ],
};

const calls: { method: string; path: string; body: unknown }[] = [];
const handlers = () => [
  http.all('*', async ({ request }) => {
    const text = request.method === 'GET' ? '' : await request.clone().text();
    calls.push({
      method: request.method,
      path: new URL(request.url).pathname,
      body: text ? (JSON.parse(text) as unknown) : null,
    });
  }),
  http.get('/api/v1/instances/:id/integration', () => HttpResponse.json(view)),
  http.post('/api/v1/instances/:id/integration/actions/:action', ({ params }) =>
    HttpResponse.json({ action: params.action, ok: true, message: null })
  ),
];
const mock = setupServer(...handlers());

beforeAll(() => {
  client.setConfig({ baseUrl: window.location.origin });
  mock.listen({ onUnhandledFrame: 'error' });
});
afterEach(() => {
  mock.resetHandlers(...handlers());
  calls.length = 0;
});
afterAll(() => mock.close());

function Tab() {
  const integration = useQuery({ ...queries.integration(id), refetchInterval: false });
  return integration.data ? (
    <>
      <IntegrationSummary
        serverName={server}
        info={integration.data.info}
        metrics={integration.data.metrics}
      />
      <IntegrationActions id={id} server={server} view={integration.data} />
      <IntegrationPlayers id={id} server={server} view={integration.data} />
    </>
  ) : null;
}

async function renderTab() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <Notifications />
      <Tab />
    </QueryClientProvider>
  );
  await screen.findByText('Live metrics');
}

const action = (name: string) =>
  calls.find(
    (call) =>
      call.method === 'POST' && call.path === `/api/v1/instances/${id}/integration/actions/${name}`
  );

describe('the game API tab', () => {
  it('shows the server, its metrics and its players', async () => {
    await renderTab();

    expect(screen.getAllByText('FakeGame').length).toBeGreaterThan(0);
    expect(screen.getByText('guid-1')).toBeInTheDocument();
    expect(screen.getByText('60')).toBeInTheDocument();
    expect(screen.getByText('1 / 32')).toBeInTheDocument();
    expect(screen.getByText('Vinh')).toBeInTheDocument();
    expect(screen.getByText('20')).toBeInTheDocument();
    expect(screen.getByText('13 ms')).toBeInTheDocument();
  });

  it('announces, saves and unbans through the generic action path', async () => {
    await renderTab();

    await userEvent.type(
      screen.getByRole('textbox', { name: 'Announce' }),
      'Server restart in 5 minutes'
    );
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(action('announce')).toBeDefined());
    expect(action('announce')?.body).toEqual({
      message: 'Server restart in 5 minutes',
      userId: null,
      waitTime: null,
    });

    await userEvent.click(screen.getByRole('button', { name: 'Save world' }));
    await waitFor(() => expect(action('save')).toBeDefined());

    await userEvent.type(screen.getByRole('textbox', { name: 'Unban a player' }), 'steam_1');
    await userEvent.click(screen.getByRole('button', { name: 'Unban' }));
    await waitFor(() => expect(action('unban')).toBeDefined());
    expect(action('unban')?.body).toEqual({
      message: null,
      userId: 'steam_1',
      waitTime: null,
    });
  });

  it('kicks a player with a reason, and bans from the same dialog', async () => {
    await renderTab();

    await userEvent.click(screen.getByRole('button', { name: 'Kick' }));
    let dialog = await screen.findByRole('dialog', { name: 'Kick Vinh?' });
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'afk too long');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Kick' }));

    await waitFor(() => expect(action('kick')).toBeDefined());
    expect(action('kick')?.body).toEqual({
      message: 'afk too long',
      userId: 'steam_1',
      waitTime: null,
    });

    await userEvent.click(screen.getByRole('button', { name: 'Ban' }));
    dialog = await screen.findByRole('dialog', { name: 'Ban Vinh?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(action('ban')).toBeUndefined();
  });
});
