import { IconExternalLink } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Card, Group, Stack, Text, Title } from '@mantine/core';
import type { PrerequisiteInfo } from '@/api/generated';

/** Runtimes the adapters need; a missing one links to its installer (docs/11 §15 D5: links only in M2). */
export function PrerequisitesCard({ prerequisites }: { prerequisites: PrerequisiteInfo[] }) {
  const { t } = useTranslation();
  return (
    <Card>
      <Title order={5} mb="sm">
        {t('system.prerequisites')}
      </Title>
      <Stack gap="xs">
        {prerequisites.map((prerequisite) => (
          <PrerequisiteRow key={prerequisite.id} prerequisite={prerequisite} />
        ))}
      </Stack>
    </Card>
  );
}

function PrerequisiteRow({ prerequisite }: { prerequisite: PrerequisiteInfo }) {
  const { t } = useTranslation();
  const needed = prerequisite.neededBy.length > 0;
  const [color, label] = prerequisite.installed
    ? ['green', t('system.installed')]
    : needed
      ? ['red', t('system.missing')]
      : ['gray', t('system.absent')];
  const link = /https?:\/\/\S+/.exec(prerequisite.hint)?.[0];
  return (
    <Group justify="space-between" wrap="nowrap" gap="xs">
      <Stack gap={0} miw={0}>
        <Text size="sm" ff="monospace">
          {prerequisite.id}
        </Text>
        <Text size="xs" c="dimmed" truncate>
          {needed
            ? t('system.neededBy', { adapters: prerequisite.neededBy.join(', ') })
            : prerequisite.hint}
        </Text>
      </Stack>
      <Group gap="xs" wrap="nowrap">
        {!prerequisite.installed && needed && link && (
          <Button
            component="a"
            href={link}
            target="_blank"
            rel="noreferrer"
            size="compact-xs"
            variant="light"
            rightSection={<IconExternalLink size={12} />}
          >
            {t('actions.install')}
          </Button>
        )}
        <Badge color={color}>{label}</Badge>
      </Group>
    </Group>
  );
}
