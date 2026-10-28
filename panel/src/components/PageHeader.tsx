import type { ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { Anchor, Breadcrumbs, Group, Stack, Text, Title } from '@mantine/core';

interface Crumb {
  label: string;
  to?: string;
}

/** docs/11 §3 page pattern: breadcrumbs, then title with status on the left and actions on the right. */
export function PageHeader({
  title,
  crumbs,
  status,
  description,
  actions,
}: {
  title: ReactNode;
  crumbs?: Crumb[];
  status?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <Stack gap={6} mb="lg">
      {crumbs && crumbs.length > 0 && (
        <Breadcrumbs separatorMargin={6}>
          {crumbs.map((crumb) =>
            crumb.to ? (
              <Anchor key={crumb.label} component={Link} to={crumb.to} size="sm" c="dimmed">
                {crumb.label}
              </Anchor>
            ) : (
              <Text key={crumb.label} size="sm" c="dimmed">
                {crumb.label}
              </Text>
            )
          )}
        </Breadcrumbs>
      )}
      <Group justify="space-between" align="center" gap="sm" wrap="wrap">
        <Group gap="sm" align="center" wrap="nowrap" miw={0}>
          <Title order={2} fz={{ base: 'h3', sm: 'h2' }} lineClamp={1}>
            {title}
          </Title>
          {status}
        </Group>
        {actions && <Group gap="xs">{actions}</Group>}
      </Group>
      {description && (
        <Text c="dimmed" size="sm">
          {description}
        </Text>
      )}
    </Stack>
  );
}
