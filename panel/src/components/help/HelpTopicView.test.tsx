import { render, screen, within } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import { HelpTopicView } from './HelpTopicView';
import type { HelpTopic } from './types';

const topic: HelpTopic = {
  summary: 'The short answer.',
  body: [
    { p: 'A paragraph.' },
    { list: ['First item', 'Second item'] },
    { tip: 'A useful tip.' },
    { warning: 'A careful warning.' },
  ],
  values: [
    { value: 'stable', text: 'Full releases.' },
    { value: 'rc', text: 'Release candidates too.' },
  ],
  tested: 'Tried on the VM, 2026-10-28.',
  verified: false,
  sources: [
    {
      title: 'A manual',
      url: 'https://example.org/manual',
      publisher: 'Example',
      checked: '2026-10-29',
    },
  ],
};

describe('HelpTopicView', () => {
  it('renders every part of docs/12 §3, with the sources last', () => {
    render(<HelpTopicView topic={topic} defaultValue="stable" />);

    expect(screen.getByRole('heading', { name: 'In short' })).toBeInTheDocument();
    expect(screen.getByText('The short answer.')).toBeInTheDocument();
    expect(screen.getByText('A paragraph.')).toBeInTheDocument();
    expect(screen.getByText('Second item')).toBeInTheDocument();
    expect(screen.getByText(/A useful tip\./)).toHaveTextContent('Tip: A useful tip.');
    expect(screen.getByText(/A careful warning\./)).toHaveTextContent(
      'Warning: A careful warning.'
    );
    expect(screen.getByRole('heading', { name: 'What we tested' })).toBeInTheDocument();
    expect(screen.getByText('Tried on the VM, 2026-10-28.')).toBeInTheDocument();

    const rows = screen.getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(within(rows[1]!).getByText('default')).toBeInTheDocument();
    expect(within(rows[2]!).queryByText('default')).not.toBeInTheDocument();

    const link = screen.getByRole('link', { name: /A manual/ });
    expect(link).toHaveAttribute('href', 'https://example.org/manual');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noreferrer');
    expect(screen.getByText('Example · checked 2026-10-29')).toBeInTheDocument();

    const headings = screen.getAllByRole('heading').map((heading) => heading.textContent);
    expect(headings.at(-1)).toBe('Sources');
  });

  it('opens related topics through its callback', async () => {
    const open = vi.fn();
    render(
      <HelpTopicView
        topic={{ ...topic, values: undefined, tested: undefined }}
        related={[{ id: 'system.channel', title: 'Update channel' }]}
        onOpenRelated={open}
      />
    );

    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'What we tested' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Update channel' }));
    expect(open).toHaveBeenCalledWith('system.channel');
  });
});
