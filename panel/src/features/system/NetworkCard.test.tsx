import { act } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import { queries } from '@/api/queries';
import { HelpProvider } from '@/components/help/HelpProvider';
import { NetworkCard } from './NetworkCard';

// docs/12 §4: the network form is a Modal that steps aside while the help drawer is open over it.
function renderCard() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false } },
  });
  queryClient.setQueryData(queries.systemSettings().queryKey, {
    panelAccessNetworks: ['26.0.0.0/8', '100.64.0.0/10'],
    panelAccessNetworksDefault: true,
    vpnRanges: ['26.0.0.0/8', '100.64.0.0/10'],
    vpnRangesDefault: true,
    defaultVpnRanges: ['26.0.0.0/8', '100.64.0.0/10'],
  });
  render(
    <QueryClientProvider client={queryClient}>
      <HelpProvider>
        <NetworkCard hosts={[]} />
      </HelpProvider>
    </QueryClientProvider>
  );
}

describe('help over the network settings dialog', () => {
  it('closes only the help drawer on Escape, keeps Tab in it and returns focus to the trigger', async () => {
    renderCard();
    await userEvent.click(screen.getByRole('button', { name: 'Change' }));
    const dialog = await screen.findByRole('dialog', { name: 'Who can reach GameWarden' });
    const trigger = within(dialog).getByRole('button', { name: 'About Game ports open to' });

    await userEvent.click(trigger);
    const help = await screen.findByRole('dialog', { name: 'Game ports open to' });
    await waitFor(() => expect(help).toContainElement(document.activeElement as HTMLElement));
    for (let step = 0; step < 12; step += 1) {
      await userEvent.tab();
      expect(help).toContainElement(document.activeElement as HTMLElement);
    }

    await userEvent.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Game ports open to' })).not.toBeInTheDocument()
    );
    expect(screen.getByRole('dialog', { name: 'Who can reach GameWarden' })).toBeInTheDocument();
    await waitFor(() => expect(trigger).toHaveFocus());
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(trigger).toHaveFocus();

    // The dialog works as before: its inputs keep their names, and Escape closes it.
    const ranges = within(dialog).getByRole('combobox', { name: 'Game ports open to' });
    await userEvent.type(ranges, '10.8.0.0/24{Enter}');
    expect(within(dialog).getByText('10.8.0.0/24')).toBeInTheDocument();
    // Escape in the ranges input belongs to its suggestions (Mantine's Combobox); from a button it closes the dialog.
    act(() => within(dialog).getByRole('button', { name: 'Save' }).focus());
    await userEvent.keyboard('{Escape}');
    await waitFor(() =>
      expect(
        screen.queryByRole('dialog', { name: 'Who can reach GameWarden' })
      ).not.toBeInTheDocument()
    );
  });
});
