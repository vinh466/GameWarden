import { renderWithRouter, screen, userEvent } from '@test-utils';
import type { SystemInfo } from '@/api/generated';
import { GettingStarted } from './GettingStarted';

const system = {
  dataRoot: 'C:\\GameWarden',
  prerequisites: [
    { id: 'vcredist-x64', installed: true, hint: '', neededBy: ['valheim'] },
    { id: 'directx-legacy', installed: false, hint: '', neededBy: [] },
  ],
} as SystemInfo;

describe('GettingStarted', () => {
  it('lists the last first-run steps with one primary action and the data folder', async () => {
    await renderWithRouter(<GettingStarted system={system} onDismiss={() => {}} />);

    expect(screen.getByText('Getting started')).toBeInTheDocument();
    expect(screen.getByText('Owner account created')).toBeInTheDocument();
    expect(screen.getByText('Prerequisites ready')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Create' })).toHaveAttribute(
      'href',
      '/servers?new=true'
    );
    expect(screen.getByText('C:\\GameWarden')).toBeInTheDocument();
  });

  it('asks for the prerequisites when one needed is missing, and can be dismissed', async () => {
    const onDismiss = vi.fn();
    await renderWithRouter(
      <GettingStarted
        system={{
          ...system,
          prerequisites: [
            { id: 'vcredist-x64', installed: false, hint: '', neededBy: ['valheim'] },
          ],
        }}
        onDismiss={onDismiss}
      />
    );

    expect(screen.getByText('Install the prerequisites')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Dismiss getting started' }));
    expect(onDismiss).toHaveBeenCalled();
  });
});
