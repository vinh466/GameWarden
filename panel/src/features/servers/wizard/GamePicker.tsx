import { IconDeviceGamepad2 } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Group, SimpleGrid, Stack, Text, ThemeIcon, UnstyledButton } from '@mantine/core';
import type { AdapterSummary } from '@/api/generated';
import classes from './GamePicker.module.css';

/** Step 1: one card per adapter. */
export function GamePicker({
  adapters,
  selected,
  onPick,
}: {
  adapters: AdapterSummary[];
  selected?: string;
  onPick: (adapter: AdapterSummary) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <Text size="sm" c="dimmed" mb="md">
        {t('wizard.pickGame')}
      </Text>
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        {adapters.map((candidate) => (
          <UnstyledButton
            key={candidate.id}
            className={classes.game}
            data-selected={selected === candidate.id || undefined}
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
