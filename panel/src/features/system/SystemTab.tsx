import type { ReactNode } from 'react';
import { Group, Stack, Text } from '@mantine/core';

/**
 * The same top line on every System tab (docs/11 §4): what the view is for on the left, its actions on the right,
 * then the content. The page title and the tab list already name the view, so a tab never repeats a page header.
 */
export function SystemTab({
  description,
  actions,
  children,
}: {
  description?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Stack gap="lg">
      {(description || actions) && (
        <Group justify="space-between" align="flex-end" gap="sm" wrap="wrap">
          {description && (
            <Text c="dimmed" size="sm" maw={760}>
              {description}
            </Text>
          )}
          {actions && (
            <Group gap="xs" wrap="wrap" ml="auto">
              {actions}
            </Group>
          )}
        </Group>
      )}
      {children}
    </Stack>
  );
}
