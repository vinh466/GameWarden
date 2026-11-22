import { IconAlertTriangle, IconCircleCheck, IconServer2 } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Group, Skeleton, Text } from '@mantine/core';
import { ButtonLink } from '@/components/links';
import { formatWhen } from '@/components/RelativeTime';
import { nowSummary } from './now';
import { useAttention } from './useAttention';

/**
 * The dashboard's first line (docs/22 §7.1): the answer to "is everything okay?" before any statistic. Problems lead
 * to the attention card below; a healthy panel says how many servers and jobs are live.
 */
export function NowLine() {
  const { t } = useTranslation();
  const { items, error, servers, jobsRunning, updatedAt, retry } = useAttention();

  if (error) {
    return (
      <Alert color="red" variant="light" icon={<IconAlertTriangle aria-hidden />}>
        <Group gap="xs" justify="space-between" wrap="wrap">
          <Text size="sm">{t('dashboard.now.unavailable')}</Text>
          <Button size="compact-sm" variant="subtle" onClick={retry}>
            {t('actions.retry')}
          </Button>
        </Group>
      </Alert>
    );
  }

  if (!items) {
    return <Skeleton h={44} radius="md" />;
  }

  const summary = nowSummary(items, servers, jobsRunning);
  if (summary.kind === 'problems') {
    return (
      <Alert
        color="red"
        variant="light"
        icon={<IconAlertTriangle aria-hidden />}
        role="status"
        aria-live="polite"
      >
        <Group gap="xs" justify="space-between" wrap="wrap">
          <Text size="sm" fw={500}>
            {t('dashboard.now.problems', { count: summary.count })}
          </Text>
          <Button component="a" href="#attention" size="compact-sm" variant="subtle">
            {t('dashboard.now.review')}
          </Button>
        </Group>
      </Alert>
    );
  }

  if (summary.servers === 0) {
    return (
      <Alert color="blue" variant="light" icon={<IconServer2 aria-hidden />}>
        <Group gap="xs" justify="space-between" wrap="wrap">
          <Text size="sm">{t('dashboard.now.empty')}</Text>
          <ButtonLink to="/servers" search={{ new: true }} size="compact-sm">
            {t('nav.newServer')}
          </ButtonLink>
        </Group>
      </Alert>
    );
  }

  const updated = updatedAt ? formatWhen(updatedAt) : null;
  return (
    <Alert
      color="green"
      variant="light"
      icon={<IconCircleCheck aria-hidden />}
      role="status"
      aria-live="polite"
    >
      <Group gap="xs" wrap="wrap">
        <Text size="sm" fw={500}>
          {t('dashboard.now.healthy', { count: summary.servers })}
        </Text>
        {summary.jobs > 0 && (
          <Text size="sm" c="dimmed">
            {t('dashboard.now.jobs', { count: summary.jobs })}
          </Text>
        )}
        {updated && (
          <Text size="xs" c="dimmed" ml="auto">
            {t('dashboard.now.updated', { time: updated })}
          </Text>
        )}
      </Group>
    </Alert>
  );
}
