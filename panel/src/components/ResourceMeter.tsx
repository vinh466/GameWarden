import { Group, Progress, Stack, Text } from '@mantine/core';

/** A labelled bar for VM resources; turns yellow from 75 % and red from 90 %. */
export function ResourceMeter({
  label,
  used,
  total,
  unit,
}: {
  label: string;
  used: number;
  total: number;
  unit: string;
}) {
  const percent = usagePercent(used, total);
  const color = usageColor(percent);
  return (
    <Stack gap={4}>
      <Group justify="space-between" gap="xs">
        <Text size="sm" c="dimmed">
          {label}
        </Text>
        <Text size="sm" fw={600} ff="monospace">
          {format(used)} / {format(total)} {unit}
        </Text>
      </Group>
      <Progress value={percent} color={color} size="md" aria-label={`${label} ${percent}%`} />
    </Stack>
  );
}

export function usagePercent(used: number, total: number) {
  return total > 0 ? Math.min(100, Math.round((used / total) * 100)) : 0;
}

/** One rule for every usage bar: yellow from 75 %, red from 90 %. */
export function usageColor(percent: number) {
  return percent >= 90 ? 'red' : percent >= 75 ? 'yellow' : 'violet';
}

function format(value: number) {
  return value >= 10_000 ? `${(value / 1024).toFixed(1)}k` : value.toLocaleString();
}
