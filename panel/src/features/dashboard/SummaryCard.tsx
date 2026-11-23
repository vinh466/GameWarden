import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Card, Group, Title } from '@mantine/core';
import { AnchorLink } from '@/components/links';

/** A dashboard section that summarises another screen and links to it. */
export function SummaryCard({
  title,
  badge,
  to,
  children,
}: {
  title: string;
  badge?: ReactNode;
  to?: '/servers' | '/activity';
  children: ReactNode;
}) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" mb="xs">
        <Group gap="xs">
          <Title order={5}>{title}</Title>
          {badge}
        </Group>
        {to && (
          <AnchorLink to={to} size="sm">
            {t('actions.viewAll')}
          </AnchorLink>
        )}
      </Group>
      {children}
    </Card>
  );
}
