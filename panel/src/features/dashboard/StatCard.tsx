import type { ReactNode } from 'react';
import type { Icon } from '@tabler/icons-react';
import { Card, Group, Text } from '@mantine/core';
import { CardLink } from '@/components/links';
import classes from './StatCard.module.css';

/** One overview number (Mantine UI's StatsGrid card); links to the screen with the details. */
export function StatCard({
  title,
  icon: StatIcon,
  value,
  unit,
  footer,
  to,
  children,
}: {
  title: string;
  icon: Icon;
  value: ReactNode;
  unit?: ReactNode;
  footer: ReactNode;
  to?: '/servers' | '/jobs' | '/system';
  children?: ReactNode;
}) {
  const body = (
    <>
      <Group justify="space-between" wrap="nowrap">
        <Text size="xs" c="dimmed" className={classes.title}>
          {title}
        </Text>
        <StatIcon className={classes.icon} size={22} stroke={1.5} aria-hidden />
      </Group>
      <Group align="flex-end" gap="xs" mt="lg" wrap="nowrap">
        <Text className={classes.value}>{value}</Text>
        {unit}
      </Group>
      {children}
      <Text fz="xs" c="dimmed" mt="xs">
        {footer}
      </Text>
    </>
  );
  return to ? (
    <CardLink to={to} className={classes.link}>
      {body}
    </CardLink>
  ) : (
    <Card>{body}</Card>
  );
}
