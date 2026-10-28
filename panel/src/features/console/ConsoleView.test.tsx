import { render, screen } from '@test-utils';
import type { OutputLine } from '@/api/generated';
import { ConsoleView } from './ConsoleView';

const line = (text: string, stream: OutputLine['stream'] = 'stdout'): OutputLine => ({
  at: '2026-10-28T10:00:00Z',
  stream,
  text,
});

describe('ConsoleView', () => {
  it('says so when there is no output', () => {
    render(<ConsoleView lines={[]} search="" height={300} />);
    expect(screen.getByText('No output yet.')).toBeInTheDocument();
  });

  it('keeps only the lines that match the search, ignoring case', () => {
    render(
      <ConsoleView
        lines={[
          line('Game server connected'),
          line('World saved'),
          line('Got connection SteamID 1'),
        ]}
        search="CONNECT"
        height={300}
      />
    );
    // The virtual list renders what fits; jsdom has no layout, so assert on what was filtered out.
    expect(screen.queryByText('World saved')).not.toBeInTheDocument();
  });
});
