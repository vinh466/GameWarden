import { render, screen } from '@test-utils';
import type { ActualState } from '@/api/generated';
import { stateLook, StatusBadge } from './StatusBadge';

describe('StatusBadge', () => {
  it.each<[ActualState, string]>([
    ['running', 'Running'],
    ['starting', 'Starting'],
    ['crashed', 'Crashed'],
    ['notInstalled', 'Not installed'],
  ])('shows %s as a word, not only a colour', (state, label) => {
    render(<StatusBadge state={state} />);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it('keeps status colours apart from the violet primary colour (docs/11 §5)', () => {
    const colors = Object.values(stateLook).map((look) => look.color);
    expect(colors).not.toContain('violet');
    expect(stateLook.running.color).toBe('green');
    expect(stateLook.faulted.color).toBe('red');
    expect(stateLook.starting.color).toBe('blue');
    expect(stateLook.stopped.color).toBe('gray');
  });
});
