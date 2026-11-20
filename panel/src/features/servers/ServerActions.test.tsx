import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, userEvent, within } from '@test-utils';
import { ModalsProvider } from '@mantine/modals';
import { stopInstance, type InstanceSnapshot } from '@/api/generated';
import { ServerActions } from './ServerActions';

vi.mock('@/api/generated', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/generated')>();
  return {
    ...actual,
    stopInstance: vi.fn().mockResolvedValue({ data: { message: 'Force stop requested.' } }),
  };
});

const stopping: InstanceSnapshot = {
  id: 'stuck-1',
  displayName: 'Stuck stop',
  adapterId: 'valheim',
  desiredState: 'stopped',
  state: 'stopping',
  activeJobId: null,
  runnerPid: 8123,
  gamePid: 8140,
  recentRestarts: 0,
  lastProblem: null,
  ports: { game: 2486, query: 2487 },
  operationId: 'op_5f3c9a12',
  stoppingSince: new Date(Date.now() - 48_000).toISOString(),
  hardKillAt: new Date(Date.now() + 12_000).toISOString(),
  stopPhase: 'graceful',
};

describe('ServerActions', () => {
  it('offers Force stop while an instance is stuck in Stopping and sends force=true', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <ModalsProvider>
          <ServerActions server={stopping} />
        </ModalsProvider>
      </QueryClientProvider>
    );

    await userEvent.click(screen.getByRole('button', { name: 'Force stop' }));

    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Force stop' }));

    expect(stopInstance).toHaveBeenCalledWith({
      path: { id: 'stuck-1' },
      query: { force: true },
      throwOnError: true,
    });
  });
});
