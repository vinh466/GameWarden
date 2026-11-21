import { useTranslation } from 'react-i18next';
import { Card, NumberInput, SimpleGrid, Text, TextInput, Title } from '@mantine/core';
import type { AgentAutonomySettings } from '@/api/generated';

/** How far the Agent may go on its own: the mode is fixed, the approval window and write budget are the Owner's. */
export function ApprovalsCard({
  autonomy,
  onChange,
}: {
  autonomy: AgentAutonomySettings;
  onChange: (autonomy: AgentAutonomySettings) => void;
}) {
  const { t } = useTranslation();

  return (
    <Card>
      <Title order={5} mb="sm">
        {t('system.operations.approvals')}
      </Title>
      <SimpleGrid cols={{ base: 1, sm: 3 }} spacing="md">
        <TextInput
          label={t('system.operations.mode')}
          value={autonomy.mode ?? 'approve-writes'}
          readOnly
          description={t('system.operations.modeHint')}
        />
        <NumberInput
          label={t('system.operations.approvalTimeout')}
          value={autonomy.approvalTimeoutMinutes ?? 15}
          onChange={(value) =>
            onChange({ ...autonomy, approvalTimeoutMinutes: Number(value) || 15 })
          }
          min={1}
          max={1440}
        />
        <NumberInput
          label={t('system.operations.maxWrites')}
          value={autonomy.maxWritesPerHour ?? 6}
          onChange={(value) => onChange({ ...autonomy, maxWritesPerHour: Number(value) || 6 })}
          min={1}
          max={100}
        />
      </SimpleGrid>
      <Text size="xs" c="dimmed" mt="xs">
        {t('system.operations.approvalsHint')}
      </Text>
    </Card>
  );
}
