import { IconCheck, IconX } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Group, Stack, Text, ThemeIcon } from '@mantine/core';
import type { TestState } from './types';

/** One line per check of a model or Discord test, with the latency when it ran. */
export function TestChecklist({ state }: { state: TestState }) {
  const { t } = useTranslation();
  const result = 'result' in state ? state.result : null;
  const error = 'error' in state ? state.error : null;
  const ok = result?.ok ?? false;

  return (
    <Alert
      mt="sm"
      color={ok ? 'green' : 'red'}
      title={ok ? t('system.operations.testPassed') : t('system.operations.testFailed')}
    >
      {result ? (
        <Stack gap={6}>
          {result.checks.map((check) => (
            <Group key={check.name} gap="xs" align="flex-start" wrap="nowrap">
              <ThemeIcon size={20} radius="xl" variant="light" color={check.ok ? 'green' : 'red'}>
                {check.ok ? <IconCheck size={12} /> : <IconX size={12} />}
              </ThemeIcon>
              <Stack gap={0} miw={0}>
                <Text size="sm" fw={500}>
                  {check.name}
                </Text>
                {check.detail && (
                  <Text size="xs" c="dimmed" style={{ overflowWrap: 'anywhere' }}>
                    {check.detail}
                  </Text>
                )}
              </Stack>
            </Group>
          ))}
          <Text size="xs" c="dimmed">
            {t('system.operations.testLatency', { latency: result.latencyMs })}
          </Text>
        </Stack>
      ) : (
        <Text size="sm">{error}</Text>
      )}
    </Alert>
  );
}
