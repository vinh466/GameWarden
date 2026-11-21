import { Stack, Text } from '@mantine/core';

/** A select option with a short explanation, used by the destination form's richer lists. */
export function DestinationOption({ label, description }: { label: string; description?: string }) {
  return (
    <Stack gap={0}>
      <Text size="sm">{label}</Text>
      {description && (
        <Text size="xs" c="dimmed">
          {description}
        </Text>
      )}
    </Stack>
  );
}
