import { IconDeviceGamepad2 } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Group,
  SimpleGrid,
  Skeleton,
  Stack,
  Text,
  ThemeIcon,
  UnstyledButton,
} from '@mantine/core';
import type { AdapterSummary } from '@/api/generated';
import { EmptyState } from '@/components/EmptyState';
import { ProblemAlert } from '@/components/ProblemAlert';
import classes from './GamePicker.module.css';

/** Section 1: one card per adapter; the hint turns into the error when Next is pressed without a game. */
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
    <>
      <Text size="sm" c={missing ? 'red' : 'dimmed'} role={missing ? 'alert' : undefined} mb="md">
        {t('wizard.pickGame')}
      </Text>
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        {adapters.map((candidate) => (
          <UnstyledButton
            key={candidate.id}
            className={classes.game}
            data-selected={selected === candidate.id || undefined}
            aria-pressed={selected === candidate.id}
            onClick={() => onPick(candidate)}
          >
            <Group wrap="nowrap">
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
          </UnstyledButton>
        ))}
      </SimpleGrid>
    </>
  );
}
