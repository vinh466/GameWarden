import { render, screen } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import type { AgentSettingsView } from '@/api/generated';
import { AgentFeatureCard } from './AgentFeatureCard';

const settings: AgentSettingsView = {
  enabled: true,
  requireAdminMode: false,
  diagnostics: { enabled: false, expiresAt: null, reason: null },
  admin: { enabled: false, expiresAt: null, reason: null },
  allowedNetworks: [],
  updatedAt: new Date().toISOString(),
};

describe('AgentFeatureCard', () => {
  it('turns the Admin-mode requirement on and off', async () => {
    const onRequireAdminMode = vi.fn();
    render(
      <AgentFeatureCard
        settings={settings}
        busy={false}
        onToggle={vi.fn()}
        onRequireAdminMode={onRequireAdminMode}
        onMode={vi.fn()}
        onDisableMode={vi.fn()}
      />
    );

    await userEvent.click(
      screen.getByRole('switch', { name: 'Require Admin mode for system writes' })
    );
    expect(onRequireAdminMode).toHaveBeenCalledWith(true);
  });
});
