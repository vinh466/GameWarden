import { Group, Text } from '@mantine/core';
import { AnchorLink } from '@/components/links';

/** The shield mark of public/favicon.svg with the product name. */
export function Logo() {
  return (
    <AnchorLink to="/" underline="never" c="inherit" aria-label="GameWarden">
      <Group gap={8} wrap="nowrap">
        <svg width="26" height="26" viewBox="0 0 32 32" aria-hidden>
          <path
            d="M16 2 4 6.5v8.8c0 7.3 5.1 13.6 12 15.7 6.9-2.1 12-8.4 12-15.7V6.5L16 2Z"
            fill="var(--mantine-primary-color-filled)"
          />
          <path
            d="M11 16.5 14.5 20 21.5 12"
            fill="none"
            stroke="#fff"
            strokeWidth="2.6"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
        <Text fw={700} size="lg" visibleFrom="xs">
          GameWarden
        </Text>
      </Group>
    </AnchorLink>
  );
}
