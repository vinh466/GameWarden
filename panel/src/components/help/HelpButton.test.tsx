import { render, screen, waitFor } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import { TextInput } from '@mantine/core';
import { HelpButton } from './HelpButton';
import { HelpField } from './HelpLabel';
import { HelpProvider } from './HelpProvider';

describe('HelpButton', () => {
  it('is named after the label and leaves the input its own name', () => {
    render(
      <HelpProvider>
        <HelpField label="Game ports open to" help={{ topicId: 'system.vpnRanges' }} required>
          {(id) => <TextInput id={id} description="Remote addresses" />}
        </HelpField>
      </HelpProvider>
    );

    expect(screen.getByRole('button', { name: 'About Game ports open to' })).toBeInTheDocument();
    // The button sits outside the <label>: the input's name is the label alone (the asterisk is aria-hidden).
    const input = screen.getByRole('textbox', { name: 'Game ports open to' });
    expect(input).toHaveAccessibleDescription('Remote addresses');
    expect(screen.getByText('Game ports open to').closest('label')).not.toContainElement(
      screen.getByRole('button', { name: 'About Game ports open to' })
    );
  });

  it('opens the topic in the drawer, with related topics and Back in the same drawer', async () => {
    render(
      <HelpProvider>
        <HelpButton label="Game ports open to" help={{ topicId: 'system.vpnRanges' }} />
      </HelpProvider>
    );

    const trigger = screen.getByRole('button', { name: 'About Game ports open to' });
    await userEvent.click(trigger);
    const drawer = await screen.findByRole('dialog');
    expect(drawer).toHaveAccessibleName('Game ports open to');
    // The Radmin range rests on an indirect source.
    expect(screen.getByText('Not verified yet')).toBeInTheDocument();

    await userEvent.click(
      screen.getByRole('button', { name: 'Panel networks (panel answers on)' })
    );
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Panel networks (panel answers on)');
    expect(screen.queryByText('Not verified yet')).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Back' })).toHaveFocus());

    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Game ports open to');
    expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();

    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
  });
});
