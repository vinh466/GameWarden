import dayjs from 'dayjs';
import { IconPuzzle } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Card, Stack, Text, Title } from '@mantine/core';
import type { ModsView } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';
import { ModActions } from './ModActions';
import { ModSetTable } from './ModSetTable';

/** The active set with what can be done to it (docs/13 §4.10); an empty state with the two ways to start. */
export function ActiveSetCard({
  id,
  server,
  mods,
}: {
  id: string;
  server: string;
  mods: ModsView;
}) {
  const { t } = useTranslation();
  const active = mods.active;
  const busyReason = mods.pending?.state === 'building' ? t('mods.busyReason') : null;
  const origin =
    active?.origin === 'code' || active?.origin === 'edit'
      ? t(`mods.origin.${active.origin}`)
      : active?.origin;

  return (
    <Card p={0}>
      <Stack gap="sm" p="md">
        <Stack gap={2}>
          <Title order={5}>{t('mods.activeSet')}</Title>
          {active && (
            <Text size="sm" c="dimmed">
              {t('mods.setMeta', {
                id: active.id,
                count: active.entries.length,
                time: dayjs(active.createdAt).fromNow(),
              })}
              {origin ? ` · ${origin}` : ''}
            </Text>
          )}
        </Stack>
        <ModActions id={id} server={server} mods={mods} busyReason={busyReason} />
      </Stack>
      {active ? (
        <ModSetTable id={id} server={server} mods={mods} busyReason={busyReason} />
      ) : (
        <EmptyState icon={IconPuzzle} title={t('mods.empty')} text={t('mods.emptyText')} />
      )}
    </Card>
  );
}
