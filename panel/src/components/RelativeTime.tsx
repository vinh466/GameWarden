import dayjs from 'dayjs';

export type TimeMode = 'auto' | 'date' | 'timestamp' | 'clock' | 'log';

/** A date value in the one panel format (docs/22 §9.3). The exact ISO value is always the tooltip. */
export function RelativeTime({
  value,
  mode = 'auto',
}: {
  value: string | number | Date | null | undefined;
  mode?: TimeMode;
}) {
  if (value == null) {
    return null;
  }
  const at = dayjs(value);
  if (!at.isValid()) {
    return null;
  }
  return (
    <time dateTime={at.toISOString()} title={at.toISOString()}>
      {formatTime(at, mode)}
    </time>
  );
}

/** The same format as `RelativeTime`, for text that cannot hold an element (confirm dialogs, interpolated strings). */
export function formatWhen(value: string | number | Date | null | undefined): string {
  if (value == null) {
    return '';
  }
  const at = dayjs(value);
  return at.isValid() ? formatTime(at, 'auto') : '';
}

function formatTime(at: dayjs.Dayjs, mode: TimeMode): string {
  if (mode === 'clock') {
    return at.format('HH:mm:ss');
  }
  if (mode === 'timestamp') {
    return at.format('YYYY-MM-DD HH:mm:ss');
  }
  if (mode === 'log') {
    return at.format('YYYY-MM-DD HH:mm:ss.SSS');
  }
  if (mode === 'date') {
    return at.format('lll');
  }
  // Under 24 hours a relative phrase is easier to read; older values need the date, not "37 days ago".
  return Math.abs(dayjs().diff(at, 'hour')) < 24 ? at.fromNow() : at.format('lll');
}
