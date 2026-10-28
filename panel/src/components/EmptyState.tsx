import type { ReactNode } from 'react';
import type { Icon } from '@tabler/icons-react';
import { Stack, Text, ThemeIcon, Title } from '@mantine/core';

/** An empty list with one clear next action (docs/11 §5). */
export function EmptyState({
  icon: Icon,
  title,
  text,
  action,
}: {
  icon: Icon;
  title: string;
  text?: string;
  action?: ReactNode;
}) {
  return (
    <Stack align="center" gap="xs" py="xl" ta="center">
      <ThemeIcon size={48} radius="xl" variant="light">
        <Icon size={26} aria-hidden />
      </ThemeIcon>
      <Title order={4}>{title}</Title>
      {text && (
        <Text c="dimmed" size="sm" maw={420}>
          {text}
        </Text>
      )}
      {action}
    </Stack>
  );
}
