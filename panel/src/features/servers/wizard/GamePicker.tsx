import { IconDeviceGamepad2 } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Group, Radio, SimpleGrid, Skeleton, Stack, Text, ThemeIcon } from '@mantine/core';
import type { AdapterSummary } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';
import classes from './GamePicker.module.css';

/** Section 1: one radio card per adapter; Next refuses to leave without a game (docs/22 §7.2, M7). */
export function GamePicker({
  adapters,
  selected,
  missing,
  loading,
  error,
  onRetry,
  onPick,
}: {
  adapters: AdapterSummary[];
  selected?: string;
  missing?: boolean;
  loading?: boolean;
  error?: unknown;
  onRetry?: () => void;
  onPick: (adapter: AdapterSummary) => void;
}) {
  const { t } = useTranslation();
  if (error) {
    return <ProblemAlert error={error} onRetry={onRetry} />;
  }

  if (loading) {
    return (
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        {[1, 2].map((key) => (
          <Skeleton key={key} h={82} radius="md" />
        ))}
      </SimpleGrid>
    );
  }

  if (adapters.length === 0) {
    return (
      <EmptyState
        icon={IconDeviceGamepad2}
        title={t('adapters.empty')}
        text={t('adapters.emptyText')}
      />
    );
  }

  return (
    <Radio.Group
      value={selected ?? null}
      onChange={(value) => {
        const candidate = adapters.find((adapter) => adapter.id === value);
        if (candidate) {
          onPick(candidate);
        }
      }}
      label={t('wizard.pickGame')}
      error={missing ? t('wizard.pickGameMissing') : undefined}
    >
      <SimpleGrid cols={{ base: 1, sm: 2 }} mt="xs">
        {adapters.map((candidate) => (
          <Radio.Card key={candidate.id} value={candidate.id} className={classes.game} radius="md">
            <Group wrap="nowrap" align="center">
              <Radio.Indicator />
              <ThemeIcon size={42} variant="light" radius="md">
                <IconDeviceGamepad2 size={24} />
              </ThemeIcon>
              <Stack gap={2}>
                <Text fw={600}>{candidate.name}</Text>
                <Group gap={6}>
                  <Badge size="sm" variant="outline">
                    {t('wizard.level', { level: candidate.level })}
                  </Badge>
                  <Text size="xs" c="dimmed">
                    {candidate.sourceType}
                  </Text>
                </Group>
              </Stack>
            </Group>
          </Radio.Card>
        ))}
      </SimpleGrid>
    </Radio.Group>
  );
}
