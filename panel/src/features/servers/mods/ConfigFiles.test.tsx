import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw/http';
import { setupServer } from 'msw/node';
import { ModalsProvider } from '@mantine/modals';
import { Notifications } from '@mantine/notifications';
import type { InstanceSnapshot } from '@/api/generated';
import { client } from '@/api/generated/client.gen';
import { HelpProvider } from '@/components/help/HelpProvider';
import { initialInstances } from '@/mocks/data';
import { mockSha } from '@/mocks/modConfigs';
import { modsHandlers } from '@/mocks/mods';
import { ConfigFiles } from './ConfigFiles';

// Config files edited in the Mods tab, with revisions (ADR-0021), against the mock API: Epic Loot's file has three
// revisions, the oldest what the game wrote.
const running = initialInstances()[0] as InstanceSnapshot;
const id = running.id;
const epicLoot = 'randyknapp.mods.epicloot.cfg';
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
  http.post('/api/v1/instances/:id/restart', () =>
    HttpResponse.json({ message: 'Restarting.', instance: running }, { status: 202 })
  ),
  ...modsHandlers({
    find: (candidate) => (candidate === id ? running : undefined),
    addJob: () => {},
    updateJob: () => {},
  }),
];
const server = setupServer(...handlers());

beforeAll(() => {
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

/** The config files card with the Epic Loot file open in its drawer. */
async function openFile(path = epicLoot) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <HelpProvider>
        <ModalsProvider>
          <Notifications />
          <ConfigFiles server={running} />
        </ModalsProvider>
      </HelpProvider>
    </QueryClientProvider>
  );
  await userEvent.click(await screen.findByText(path));
  return screen.findByRole('dialog', { name: path });
}

const sent = (method: string, path: string) =>
  calls.filter((call) => call.method === method && call.path === `/api/v1/instances/${id}${path}`);

async function edit(drawer: HTMLElement, text: string) {
  await userEvent.click(await within(drawer).findByRole('button', { name: 'Edit' }));
  const editor = within(drawer).getByRole('textbox', { name: `Text of ${epicLoot}` });
  await userEvent.clear(editor);
  await userEvent.type(editor, text);
  return editor;
}

describe('config files', () => {
  it('edits a file and saves it with the hash of the text it started from', async () => {
    const drawer = await openFile();
    const original = (await within(drawer).findByText(/Settings file was created/)).textContent;
    expect(within(drawer).getByRole('button', { name: 'Edit' })).toBeEnabled();

    await edit(drawer, 'General{enter}Enabled = false');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(sent('PUT', '/mods/configs/content')).toHaveLength(1));
    expect(sent('PUT', '/mods/configs/content')[0]?.body).toEqual({
      content: 'General\nEnabled = false',
      baseSha256: mockSha(original ?? ''),
    });
    expect(await screen.findByText(`${epicLoot} saved.`)).toBeInTheDocument();
    expect(await within(drawer).findByText(/Enabled = false/)).toBeInTheDocument();
    expect(within(drawer).queryByText('Saved while the server runs')).not.toBeInTheDocument();
  });

  it('keeps the text when the file changed on the server, and compares or reloads it', async () => {
    server.use(
      http.put(
        '/api/v1/instances/:id/mods/configs/content',
        () =>
          HttpResponse.json(
            { status: 409, title: 'changed', code: 'config_changed' },
            { status: 409, headers: { 'Content-Type': 'application/problem+json' } }
          ),
        { once: true }
      )
    );
    const drawer = await openFile();
    const editor = await edit(drawer, 'mine');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    expect(await within(drawer).findByText('The file changed on the server')).toBeInTheDocument();
    expect(editor).toHaveValue('mine');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Compare' }));
    const diff = within(drawer).getByRole('table', {
      name: 'Red: only in the server’s version. Green: only in your text.',
    });
    expect(within(diff).getByText('mine').closest('[data-kind]')).toHaveAttribute(
      'data-kind',
      'added'
    );

    await userEvent.click(
      within(drawer).getByRole('button', { name: 'Load the server’s version' })
    );
    await waitFor(() =>
      expect(
        (
          within(drawer).getByRole('textbox', {
            name: `Text of ${epicLoot}`,
          }) as HTMLTextAreaElement
        ).value
      ).toContain('AbilityCooldown = 45')
    );
    expect(within(drawer).queryByText('The file changed on the server')).not.toBeInTheDocument();
  });

  it('says when the running server got the change, with Restart now', async () => {
    server.use(
      http.put('/api/v1/instances/:id/mods/configs/content', () =>
        HttpResponse.json({
          path: epicLoot,
          sha256: 'a'.repeat(64),
          size: 8,
          revisionId: 200,
          running: true,
        })
      )
    );
    const drawer = await openFile();
    await edit(drawer, 'live');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    expect(await within(drawer).findByText('Saved while the server runs')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Restart now' }));
    await waitFor(() => expect(sent('POST', '/restart')).toHaveLength(1));
  });

  it('lists the revisions, compares one with the current file and restores it after a confirmation', async () => {
    const drawer = await openFile();
    await userEvent.click(within(drawer).getByRole('tab', { name: 'History' }));

    expect(await within(drawer).findByText('On the server')).toBeInTheDocument();
    expect(within(drawer).getAllByText('Edited')).toHaveLength(2);
    expect(within(drawer).getByText(/The newest 50 of each file stay/)).toBeInTheDocument();
    await userEvent.click(within(drawer).getByText('On the server'));

    expect(
      await within(drawer).findByText('Revision 101 compared with the current file')
    ).toBeInTheDocument();
    const diff = within(drawer).getByRole('table', {
      name: 'Red: only in this revision. Green: only in the current file.',
    });
    expect(
      within(diff).getByText('ItemsToMaterialsDropRatio = 0').closest('[data-kind]')
    ).toHaveAttribute('data-kind', 'removed');
    expect(within(diff).getByText('AbilityCooldown = 45').closest('[data-kind]')).toHaveAttribute(
      'data-kind',
      'added'
    );
    expect(within(drawer).getByText('+6 −1 lines')).toBeInTheDocument();

    await userEvent.click(within(drawer).getByRole('button', { name: 'Restore' }));
    const confirm = await screen.findByRole('dialog', { name: 'Restore this revision?' });
    expect(sent('POST', '/mods/configs/revisions/101/restore')).toHaveLength(0);
    await userEvent.click(within(confirm).getByRole('button', { name: 'Restore' }));

    await waitFor(() =>
      expect(sent('POST', '/mods/configs/revisions/101/restore')).toHaveLength(1)
    );
    expect(sent('POST', '/mods/configs/revisions/101/restore')[0]?.body).toEqual({
      baseSha256: expect.stringMatching(/^[0-9a-f]{64}$/),
    });
    expect(await screen.findByText(`${epicLoot} restored.`)).toBeInTheDocument();
    await waitFor(() =>
      expect(within(drawer).getByText(/Settings file was created/).textContent).not.toContain(
        'AbilityCooldown'
      )
    );
  });

  it('does not edit a file that is not text', async () => {
    const drawer = await openFile('EpicLoot/translations/English.dat');
    expect(await within(drawer).findByRole('button', { name: 'Edit' })).toBeDisabled();
  });
});
