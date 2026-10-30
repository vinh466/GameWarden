import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw/http';
import { setupServer } from 'msw/node';
import { ModalsProvider } from '@mantine/modals';
import { Notifications } from '@mantine/notifications';
import type { InstanceSnapshot, JobInfo } from '@/api/generated';
import { client } from '@/api/generated/client.gen';
import { queries } from '@/api/queries';
import { HelpProvider } from '@/components/help/HelpProvider';
import { initialInstances } from '@/mocks/data';
import { modsHandlers } from '@/mocks/mods';
import { ActiveSetCard } from './ActiveSetCard';
import { ModsOnCard } from './ModsOnCard';

// The Mods tab against the mock API (src/mocks/mods.ts), as `npm run dev:mock` shows it: a running Valheim server
// whose active set came from the group's code after its last start.
const running = initialInstances()[0] as InstanceSnapshot;
const id = running.id;
const calls: { method: string; path: string; body: unknown }[] = [];
const jobs: JobInfo[] = [];

// Fresh mock state for each test: a test's changes (an applied code, a rollback) do not leak into the next.
const handlers = () => [
  http.all('*', async ({ request }) => {
    const text = request.method === 'GET' ? '' : await request.clone().text();
    calls.push({
      method: request.method,
      path: new URL(request.url).pathname,
      body: text ? (JSON.parse(text) as unknown) : null,
    });
  }),
  http.post('/api/v1/instances/:id/restart', () =>
    HttpResponse.json({ message: 'Restarting.', instance: running }, { status: 202 })
  ),
  ...modsHandlers({
    find: (candidate) => (candidate === id ? running : undefined),
    addJob: (job) => jobs.push(job),
    updateJob: () => {},
  }),
];
const server = setupServer(...handlers());

beforeAll(() => {
  // A desktop width: mantine-datatable shows a column while its media query (or the empty one) matches.
  vi.mocked(window.matchMedia).mockImplementation(
    (query: string) =>
      ({
        matches: query === '' || query.includes('min-width'),
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }) as unknown as MediaQueryList
  );
  client.setConfig({ baseUrl: window.location.origin });
  server.listen({ onUnhandledFrame: 'error' });
});
afterEach(() => {
  server.resetHandlers(...handlers());
  calls.length = 0;
});
afterAll(() => server.close());

function Tab() {
  const mods = useQuery({ ...queries.mods(id), refetchInterval: false });
  return mods.data ? (
    <>
      <ModsOnCard server={running} mods={mods.data} />
      <ActiveSetCard id={id} server={running.displayName} mods={mods.data} />
    </>
  ) : null;
}

async function renderTab() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <HelpProvider>
        <ModalsProvider>
          <Notifications />
          <Tab />
        </ModalsProvider>
      </HelpProvider>
    </QueryClientProvider>
  );
  await screen.findByText('Active mod set');
}

const sent = (method: string, path: string) =>
  calls.filter((call) => call.method === method && call.path === `/api/v1/instances/${id}${path}`);

describe('the Mods tab', () => {
  it('shows the active set with its dependencies, a newer version, and Restart to apply', async () => {
    await renderTab();

    expect(screen.getByRole('link', { name: 'EpicLoot' })).toHaveAttribute(
      'href',
      'https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/'
    );
    expect(screen.getAllByText('dependency')).toHaveLength(3);
    expect(screen.getByText('0.15.4')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Update all (1)' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Restart now' }));
    await waitFor(() =>
      expect(calls.some((call) => call.path === `/api/v1/instances/${id}/restart`)).toBe(true)
    );
  });

  it('turns Mods off and on', async () => {
    await renderTab();
    const toggle = screen.getByRole('switch', { name: 'Mods on' });
    expect(toggle).toBeChecked();

    await userEvent.click(toggle);

    await waitFor(() => expect(toggle).not.toBeChecked());
    expect(sent('PUT', '/mods/enabled').at(-1)?.body).toEqual({ enabled: false });
    expect(await screen.findByText(/Mods off for Vikings of the North/)).toBeInTheDocument();
    await userEvent.click(toggle);
    await waitFor(() => expect(toggle).toBeChecked());
  });

  it('imports an r2modman code: preview first, then apply', async () => {
    await renderTab();
    await userEvent.click(screen.getByRole('button', { name: 'Import r2modman code' }));
    const dialog = await screen.findByRole('dialog', { name: 'Import an r2modman code' });

    const code = within(dialog).getByRole('textbox', { name: 'Profile code' });
    await userEvent.type(code, 'not a code');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    expect(await within(dialog).findByText(/looks like 0192f1c4/)).toBeInTheDocument();
    expect(sent('POST', '/mods/code-preview')).toHaveLength(0);

    await userEvent.clear(code);
    await userEvent.type(code, ' 0192F1C4-7A51-7C3E-9D0B-5E2F8A6B4C13 ');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Preview' }));
    expect(await within(dialog).findByText('Profile “Vikings 2026”')).toBeInTheDocument();
    expect(within(dialog).getByText('VikingNPC Continued')).toBeInTheDocument();
    expect(within(dialog).getByText('Deprecated')).toBeInTheDocument();
    expect(within(dialog).getByText(/outside config\//)).toBeInTheDocument();
    expect(sent('PUT', '/mods')).toHaveLength(0); // a preview changes nothing

    await userEvent.click(within(dialog).getByRole('button', { name: 'Apply' }));

    await waitFor(() =>
      expect(
        screen.queryByRole('dialog', { name: 'Import an r2modman code' })
      ).not.toBeInTheDocument()
    );
    expect(sent('PUT', '/mods').at(-1)?.body).toEqual({ previewId: '5f0c2a9e6b1d4e8f' });
  });

  it('adds a mod found by search at its latest version', async () => {
    await renderTab();
    await userEvent.click(screen.getByRole('button', { name: 'Add mod' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a mod' });

    await userEvent.type(
      within(dialog).getByRole('textbox', { name: 'Search Thunderstore' }),
      'seasons'
    );
    const add = await within(dialog).findByRole(
      'button',
      { name: 'Add 1.10.5: shudnal-Seasons' },
      { timeout: 3000 }
    );
    expect(within(dialog).getByText(/Four customizable seasons/)).toBeInTheDocument();
    await userEvent.click(add);

    await waitFor(() => expect(sent('PUT', '/mods')).toHaveLength(1));
    expect(sent('PUT', '/mods')[0]?.body).toEqual({
      entries: [
        { package: 'RandyKnapp-EpicLoot', version: '0.15.3', enabled: true },
        { package: 'SephrinMods-VikingNPC_Continued', version: '0.4.2', enabled: true },
        { package: 'Advize-PlantEverything', version: '1.21.4', enabled: false },
        { package: 'shudnal-Seasons', version: '1.10.5', enabled: true },
      ],
    });
  });

  it('adds a mod by its exact name and refuses one that is not a package name', async () => {
    await renderTab();
    await userEvent.click(screen.getByRole('button', { name: 'Add mod' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a mod' });
    const exact = within(dialog).getByRole('textbox', { name: 'Or add by exact name' });

    await userEvent.type(exact, 'EquipmentAndQuickSlots');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));
    expect(await within(dialog).findByText(/Write it as Namespace-Name/)).toBeInTheDocument();

    await userEvent.clear(exact);
    await userEvent.type(exact, 'RandyKnapp-EquipmentAndQuickSlots@3.2.0');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    await waitFor(() => expect(sent('PUT', '/mods')).toHaveLength(1));
    const body = sent('PUT', '/mods')[0]?.body as { entries: unknown[] };
    expect(body.entries.at(-1)).toEqual({
      package: 'RandyKnapp-EquipmentAndQuickSlots',
      version: '3.2.0',
      enabled: true,
    });
  });

  it('shows the share code for friends and makes a new one', async () => {
    await renderTab();
    await userEvent.click(screen.getByRole('button', { name: 'Share code for friends' }));
    const dialog = await screen.findByRole('dialog', { name: 'Share code for friends' });

    expect(within(dialog).getByText('019a2b7e-55c1-7d0e-a8f3-1c9e4b2d6a70')).toBeInTheDocument();
    expect(within(dialog).getByText(/Import profile → From code/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Config files are not in the code/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Copy' })).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Make a new code' }));

    await waitFor(() =>
      expect(
        within(dialog).queryByText('019a2b7e-55c1-7d0e-a8f3-1c9e4b2d6a70')
      ).not.toBeInTheDocument()
    );
    expect(sent('POST', '/mods/share-code')).toHaveLength(1);
  });

  it('asks before going back to the previous set', async () => {
    await renderTab();
    const back = screen.getByRole('button', { name: 'Back to the previous set' });

    await userEvent.click(back);
    const confirm = await screen.findByRole('dialog', { name: 'Back to the previous set?' });
    expect(within(confirm).getByText(/The previous set \(4 mods/)).toBeInTheDocument();
    await userEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));
    expect(sent('POST', '/mods/rollback')).toHaveLength(0);

    await userEvent.click(back);
    const again = await screen.findByRole('dialog', { name: 'Back to the previous set?' });
    await userEvent.click(within(again).getByRole('button', { name: 'Go back' }));
    await waitFor(() => expect(sent('POST', '/mods/rollback')).toHaveLength(1));
  });
});
