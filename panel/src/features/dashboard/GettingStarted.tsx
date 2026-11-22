import type { ReactNode } from 'react';
import { IconCircle, IconCircleCheck, IconPlus, IconX } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Card, Group, Stack, Text, Title } from '@mantine/core';
import type { SystemInfo } from '@/api/generated';
import { ButtonLink } from '@/components/links';

/**
 * The zero-server dashboard card (docs/07 §4, docs/22 §7.1): the last steps of the first run, with one primary
 * action. Dismissing it is a panel preference; it never hides a working server.
 */
export function GettingStarted({
  system,
  onDismiss,
}: {
  system: SystemInfo;
  onDismiss: () => void;
}) {
  const { t } = useTranslation();
  const prerequisitesReady = system.prerequisites.every(
    (prerequisite) => prerequisite.installed || prerequisite.neededBy.length === 0
  );

  return (
    <Card>
      <Group justify="space-between" align="flex-start" mb="md" wrap="nowrap">
        <Title order={5}>{t('dashboard.gettingStarted.title')}</Title>
        <ActionIcon
          variant="subtle"
          color="gray"
          aria-label={t('dashboard.gettingStarted.dismiss')}
          onClick={onDismiss}
        >
          <IconX size={16} />
        </ActionIcon>
      </Group>
      <Stack gap="sm">
        <Step done>{t('dashboard.gettingStarted.account')}</Step>
        <Step
          done={prerequisitesReady}
          action={
            prerequisitesReady ? null : (
              <ButtonLink size="compact-xs" variant="default" to="/system">
                {t('actions.open')}
              </ButtonLink>
            )
          }
        >
          {t(
            prerequisitesReady
              ? 'dashboard.gettingStarted.prerequisites'
              : 'dashboard.gettingStarted.prerequisitesTodo'
          )}
        </Step>
        <Step
          action={
            <ButtonLink
              size="compact-xs"
              to="/servers"
              search={{ new: true }}
              leftSection={<IconPlus size={14} />}
            >
              {t('dashboard.gettingStarted.createAction')}
            </ButtonLink>
          }
        >
          {t('dashboard.gettingStarted.create')}
        </Step>
        <Step
          action={
            <ButtonLink size="compact-xs" variant="default" to="/system/notifications">
              {t('actions.open')}
            </ButtonLink>
          }
        >
          {t('dashboard.gettingStarted.notifications')}
        </Step>
        <Step
          action={
            <ButtonLink size="compact-xs" variant="default" to="/system">
              {t('actions.open')}
            </ButtonLink>
          }
        >
          <Stack gap={0}>
            <Text size="sm">{t('dashboard.gettingStarted.data')}</Text>
            <Text size="xs" c="dimmed" ff="monospace" truncate>
              {system.dataRoot}
            </Text>
          </Stack>
        </Step>
      </Stack>
    </Card>
  );
}

function Step({
  done = false,
  action,
  children,
}: {
  done?: boolean;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Group gap="sm" wrap="nowrap" align="center">
      {done ? (
        <IconCircleCheck size={18} color="var(--mantine-color-green-6)" aria-hidden />
      ) : (
        <IconCircle size={18} color="var(--mantine-color-dimmed)" aria-hidden />
      )}
      <div style={{ flex: 1 }}>{children}</div>
      {action}
    </Group>
  );
}
