import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import { queries } from '@/api/queries';
import { HelpProvider } from '@/components/help/HelpProvider';
import { adapters, system } from '@/mocks/data';
import { NewServerDrawer } from './NewServerDrawer';

// docs/12 §4: the help drawer opens over the wizard; the wizard steps aside while it is open.
function renderWizard(onClose: () => void) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false, refetchInterval: false } },
  });
  queryClient.setQueryData(queries.adapters().queryKey, { adapters, problems: [] });
  queryClient.setQueryData(queries.system().queryKey, system);
  render(
    <QueryClientProvider client={queryClient}>
      <HelpProvider>
        <NewServerDrawer opened onClose={onClose} />
      </HelpProvider>
    </QueryClientProvider>
  );
}

describe('help over the new server wizard', () => {
  it('closes only the help drawer on Escape, keeps Tab in it and returns focus to the trigger', async () => {
    const onClose = vi.fn();
    renderWizard(onClose);
    const wizard = screen.getByRole('dialog', { name: 'New server' });
    await userEvent.click(within(wizard).getByText('Valheim'));
    const trigger = await within(wizard).findByRole('button', { name: 'About Id' });

    await userEvent.click(trigger);
    const help = await screen.findByRole('dialog', { name: 'Id' });
    await waitFor(() => expect(help).toContainElement(document.activeElement as HTMLElement));

    // Tab and Shift+Tab cycle inside the help drawer; the wizard's trap does not pull focus back.
    for (let step = 0; step < 6; step += 1) {
      await userEvent.tab();
      expect(help).toContainElement(document.activeElement as HTMLElement);
    }

    await userEvent.tab({ shift: true });
    expect(help).toContainElement(document.activeElement as HTMLElement);

    await userEvent.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Id' })).not.toBeInTheDocument()
    );
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog', { name: 'New server' })).toBeInTheDocument();
    await waitFor(() => expect(trigger).toHaveFocus());
    // Focus stays on the trigger once the wizard's trap is back on.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(trigger).toHaveFocus();

    // The wizard works as before: its fields take input, and Escape closes it again.
    const id = within(wizard).getByRole('textbox', { name: 'Id' });
    await userEvent.clear(id);
    await userEvent.type(id, 'valheim-test');
    expect(id).toHaveValue('valheim-test');
    await userEvent.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
