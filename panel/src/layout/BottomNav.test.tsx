import { renderWithRouter, screen, userEvent, waitFor } from '@test-utils';
import { BottomNav } from './BottomNav';

describe('BottomNav', () => {
  it('shows the four destinations and marks the current one', async () => {
    await renderWithRouter(<BottomNav />);

    expect(screen.getByRole('navigation', { name: 'Sections' })).toBeInTheDocument();
    for (const label of ['Dashboard', 'Servers', 'Activity', 'System']) {
      expect(screen.getByRole('link', { name: label })).toBeInTheDocument();
    }
    expect(screen.getByRole('link', { name: 'Dashboard' })).toHaveAttribute('data-active');
  });

  it('moves the active state to the destination the user picks', async () => {
    await renderWithRouter(<BottomNav />);

    await userEvent.click(screen.getByRole('link', { name: 'Activity' }));
    await waitFor(() =>
      expect(screen.getByRole('link', { name: 'Activity' })).toHaveAttribute('data-active')
    );
    expect(screen.getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('data-active');
  });
});
