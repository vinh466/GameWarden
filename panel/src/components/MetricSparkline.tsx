import { useId } from 'react';
import { Box, type MantineColor } from '@mantine/core';
import type { MetricPoint } from '@/api/generated';

/**
 * The last ten minutes of one metric as a line over a faint area. A plain SVG instead of @mantine/charts: Recharts
 * would add about 85 KB gzip to the dashboard and break the first-load budget (docs/11 §10 rule 9).
 * The scale starts at 0, so a flat line means steady, not "near the top"; CPU spans at least 0–10 %.
 */
export function MetricSparkline({
  history,
  field,
  color,
  h = 36,
  label,
}: {
  history: MetricPoint[];
  field: 'cpuPercent' | 'ramMb';
  color: MantineColor;
  h?: number;
  label: string;
}) {
  const gradient = useId();
  if (history.length < 2) {
    return <Box h={h} />;
  }

  const values = history.map((point) => point[field]);
  const top = Math.max(field === 'cpuPercent' ? 10 : 1, ...values) * 1.15;
  const points = values.map(
    (value, i) =>
      `${((i / (values.length - 1)) * 100).toFixed(2)},${(100 - (value / top) * 100).toFixed(2)}`
  );
  const stroke = `var(--mantine-color-${color}-filled)`;

  return (
    <Box h={h} mt="xs">
      <svg
        width="100%"
        height="100%"
        viewBox="0 0 100 100"
        preserveAspectRatio="none"
        role="img"
        aria-label={label}
      >
        <defs>
          <linearGradient id={gradient} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor={stroke} stopOpacity={0.3} />
            <stop offset="100%" stopColor={stroke} stopOpacity={0} />
          </linearGradient>
        </defs>
        <polygon points={`0,100 ${points.join(' ')} 100,100`} fill={`url(#${gradient})`} />
        <polyline
          points={points.join(' ')}
          fill="none"
          stroke={stroke}
          strokeWidth={1.5}
          vectorEffect="non-scaling-stroke"
          strokeLinejoin="round"
        />
      </svg>
    </Box>
  );
}

/** RAM as people read it: MB below 1 GB, GB with one decimal above. */
export function formatRam(mb: number) {
  return mb < 1024 ? `${Math.round(mb)} MB` : `${(mb / 1024).toFixed(1)} GB`;
}
