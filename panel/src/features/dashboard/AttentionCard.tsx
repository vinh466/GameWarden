import dayjs from 'dayjs';
import {
  IconAlertTriangle,
  IconCircleCheck,
  IconCircleX,
  IconPackage,
  IconPower,
  IconWorldOff,
} from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Group, Skeleton, Stack, Text, ThemeIcon } from '@mantine/core';
import { jobTypeLabel } from '@/components/JobProgress';
import { NavLinkLink } from '@/components/links';
import type { AttentionItem } from './attention';
import { SummaryCard } from './SummaryCard';
import { useAttention } from './useAttention';

/** Problems worth a look, each linking to where it is fixed. */
export function AttentionCard() {
  const { t } = useTranslation();
  const items = useAttention();

  return (
    <SummaryCard
      title={t('dashboard.attention.title')}
      badge={
        items && items.length > 0 ? (
          <Badge color="red" circle>
            {items.length}
          </Badge>
        ) : undefined
      }
    >
      {!items ? (
        <Skeleton h={60} />
      ) : items.length === 0 ? (
        <Group gap="xs" c="green">
          <IconCircleCheck size={18} aria-hidden />
          <Text size="sm">{t('dashboard.attention.none')}</Text>
        </Group>
      ) : (
        <Stack gap={2}>
          {items.map((item) => (
            <AttentionRow key={rowKey(item)} item={item} />
          ))}
        </Stack>
      )}
    </SummaryCard>
  );
}

function rowKey(item: AttentionItem) {
  switch (item.kind) {
    case 'core':
      return 'core';
    case 'server':
      return `server-${item.server.id}`;
    case 'job':
      return `job-${item.job.id}`;
    case 'prerequisite':
      return `prerequisite-${item.id}`;
    case 'dns':
      return `dns-${item.host}`;
  }
}

function AttentionRow({ item }: { item: AttentionItem }) {
  const { t } = useTranslation();
  const icon = (Icon: typeof IconAlertTriangle, color: string) => (
    <ThemeIcon size={26} radius="xl" variant="light" color={color}>
      <Icon size={15} />
    </ThemeIcon>
  );
  switch (item.kind) {
    case 'core':
      return (
        <NavLinkLink
          to="/activity"
          search={{ type: 'system' }}
          label={t('dashboard.attention.coreRecovered', { time: dayjs(item.at).format('LT') })}
          description={item.message}
          leftSection={icon(IconPower, 'orange')}
        />
      );
    case 'server':
      return (
        <NavLinkLink
          to="/servers/$id"
          params={{ id: item.server.id }}
          label={`${item.server.displayName}: ${t(`state.${item.server.state}`)}`}
          description={item.server.lastProblem}
          leftSection={icon(IconAlertTriangle, 'red')}
        />
      );
    case 'job':
      return (
        <NavLinkLink
          to="/jobs"
          search={item.job.instanceId ? { server: item.job.instanceId } : {}}
          label={`${jobTypeLabel(t, item.job.type)} · ${item.job.instanceId ?? ''}: ${t('jobs.state.failed')}`}
          description={item.job.message}
          leftSection={icon(IconCircleX, 'red')}
        />
      );
    case 'prerequisite':
      return (
        <NavLinkLink
          to="/system"
          label={`${item.id}: ${t('system.missing')}`}
          description={t('system.neededBy', { adapters: item.neededBy.join(', ') })}
          leftSection={icon(IconPackage, 'yellow')}
        />
      );
    case 'dns':
      return (
        <NavLinkLink
          to="/system"
          label={`${item.host}: ${item.status === 'blocked' ? t('system.dnsBlocked') : t('system.dnsUnresolved')}`}
          description={item.purpose}
          leftSection={icon(IconWorldOff, 'yellow')}
        />
      );
  }
}
