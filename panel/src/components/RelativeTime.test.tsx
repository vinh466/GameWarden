import { render, screen } from '@test-utils';
import { RelativeTime, formatWhen } from './RelativeTime';

const now = new Date('2026-11-22T12:00:00.000Z');

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(now);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('RelativeTime', () => {
  it('reads a recent value as a relative phrase and keeps the ISO value in the tooltip', () => {
    const value = new Date(now.getTime() - 3 * 60 * 1000).toISOString();
    render(<RelativeTime value={value} />);

    const time = screen.getByText('3 minutes ago');
    expect(time).toHaveAttribute('dateTime', value);
    expect(time).toHaveAttribute('title', value);
  });

  it('shows the locale date once a value is older than 24 hours', () => {
    const value = new Date(now.getTime() - 48 * 60 * 60 * 1000).toISOString();
    render(<RelativeTime value={value} />);

    expect(screen.getByText(/Nov 20, 2026/)).toBeInTheDocument();
    expect(screen.queryByText(/ago|days ago/)).not.toBeInTheDocument();
  });

  it('keeps technical screens on a fixed timestamp inside the component', () => {
    render(<RelativeTime value={now.toISOString()} mode="timestamp" />);

    expect(screen.getByText(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/)).toBeInTheDocument();
  });

  it('renders nothing for a missing or invalid value', () => {
    const { container } = render(
      <>
        <RelativeTime value={null} />
        <RelativeTime value="not a date" />
      </>
    );
    expect(container.querySelector('time')).toBeNull();
  });

  it('formats text for dialogs with the same policy as the component', () => {
    expect(formatWhen(new Date(now.getTime() - 2 * 60 * 60 * 1000))).toBe('2 hours ago');
    expect(formatWhen(null)).toBe('');
  });
});
