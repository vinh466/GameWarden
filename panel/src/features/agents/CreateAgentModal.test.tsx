import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, userEvent } from '@test-utils';
import type { AgentPermissionInfo, AgentPermissionPreset } from '@/api/generated';
import { CreateAgentModal } from './CreateAgentModal';

const permissions: AgentPermissionInfo[] = [
  { key: 'agent.status.read', group: 'observe', description: 'Read status.', risk: 'low' },
  { key: 'agent.jobs.read', group: 'observe', description: 'Read jobs.', risk: 'low' },
  { key: 'agent.instances.start', group: 'operate', description: 'Start a server.', risk: 'high' },
  {
    key: 'agent.instances.delete',
    group: 'admin',
    description: 'Delete a server.',
    risk: 'critical',
  },
];

const presets: AgentPermissionPreset[] = [
  {
    key: 'observer',
    name: 'Observer',
    description: 'Read only.',
    permissions: ['agent.status.read', 'agent.jobs.read'],
  },
  {
    key: 'operator',
    name: 'Operator',
    description: 'Run the servers.',
    permissions: ['agent.status.read', 'agent.jobs.read', 'agent.instances.start'],
  },
];

function renderModal() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <CreateAgentModal
        opened
        onClose={() => {}}
        permissions={permissions}
        presets={presets}
        onCreated={() => {}}
      />
    </QueryClientProvider>
  );
}

describe('CreateAgentModal permissions', () => {
  it('starts on Operator and sums the selected permissions', () => {
    renderModal();

    expect(screen.getByRole('radio', { name: /Operator/ })).toBeChecked();
    expect(screen.getByText('3 of 4 permissions')).toBeInTheDocument();
    expect(screen.getByText('Operator')).toBeInTheDocument();
  });

  it('switches to Custom when a permission changes', async () => {
    renderModal();

    await userEvent.click(screen.getByRole('checkbox', { name: /agent.instances.delete/ }));

    expect(screen.getByRole('radio', { name: /Custom/ })).toBeChecked();
    expect(screen.getByText('4 of 4 permissions')).toBeInTheDocument();
  });
});
