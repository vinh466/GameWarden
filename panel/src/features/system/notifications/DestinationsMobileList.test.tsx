import { render, screen } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import type { NotificationDestinationView } from '@/api/generated';
import { DestinationsMobileList } from './DestinationsMobileList';

const destination: NotificationDestinationView = {
  id: 'discord-owner',
  name: 'Owner Discord',
  channel: 'discord',
  enabled: true,
  mode: 'instant',
  language: 'en',
  minimumSeverity: 'warning',
  topics: [],
  instances: [],
  mentionRoleId: null,
  panelUrl: null,
  intervalSec: 0,
  digestSec: 0,
  webhookSet: true,
};

describe('DestinationsMobileList', () => {
  it('keeps every destination action reachable instead of hiding them in a wide table', async () => {
    const onEdit = vi.fn();
    render(
      <DestinationsMobileList
        destinations={[destination]}
        deliveries={[]}
        busy={false}
        onEdit={onEdit}
        onTest={vi.fn()}
        onToggle={vi.fn()}
        onDelete={vi.fn()}
      />
    );

    expect(screen.getByText('Owner Discord')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Test' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Edit' }));
    expect(onEdit).toHaveBeenCalledWith(destination);
  });
});
