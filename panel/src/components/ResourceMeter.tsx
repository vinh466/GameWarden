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
  const percent = total > 0 ? Math.min(100, Math.round((used / total) * 100)) : 0;
  const color = percent >= 90 ? 'red' : percent >= 75 ? 'yellow' : 'violet';
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

function format(value: number) {
  return value >= 10_000 ? `${(value / 1024).toFixed(1)}k` : value.toLocaleString();
}
