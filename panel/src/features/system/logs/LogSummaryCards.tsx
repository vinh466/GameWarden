import { useTranslation } from 'react-i18next';
import { Card, SimpleGrid, Text, Title } from '@mantine/core';
import type { SystemLogSummary } from '@/api/generated';

/** The one-hour summary above the log table (docs/19). */
export function LogSummaryCards({ summary }: { summary?: SystemLogSummary }) {
  const { t } = useTranslation();
  const count = (level: string) => summary?.levels.find((item) => item.key === level)?.count ?? 0;
  return (
    <SimpleGrid cols={{ base: 2, sm: 4 }}>
      <Summary label={t('system.logs.summaryTotal')} value={summary?.total} />
      <Summary label={t('system.logs.summaryErrors')} value={count('error')} color="red" />
      <Summary label={t('system.logs.summaryCritical')} value={count('critical')} color="dark" />
      <Summary
        label={t('system.logs.summaryWindow')}
        value={t('system.logs.minutes', { count: 60 })}
      />
    </SimpleGrid>
  );
}

function Summary({
  label,
  value,
  color,
}: {
  label: string;
  value: number | string | undefined;
  color?: string;
}) {
  return (
    <Card>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Title order={4} c={color}>
        {value ?? '—'}
      </Title>
    </Card>
  );
}
