import dayjs from 'dayjs';
import { IconRobot, IconShieldCheck } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Card, Divider, Group, Stack, Switch, Text, Title } from '@mantine/core';
import type { AgentSettingsView } from '@/api/generated';

/** The master switch and the two time-boxed modes, in one card (docs/15). */
export function AgentFeatureCard({
  settings,
  busy,
  onToggle,
  onRequireAdminMode,
  onMode,
  onDisableMode,
}: {
  settings: AgentSettingsView;
  busy: boolean;
  onToggle: (enabled: boolean) => void;
  onRequireAdminMode: (required: boolean) => void;
  onMode: (mode: 'diagnostics' | 'admin') => void;
  onDisableMode: (mode: 'diagnostics' | 'admin') => void;
}) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" align="flex-start" gap="md">
        <Stack gap={4}>
          <Group gap="xs">
            <IconRobot size={20} />
            <Title order={5}>{t('agents.feature')}</Title>
          </Group>
          <Text size="sm" c="dimmed">
            {t('agents.featureText')}
          </Text>
        </Stack>
        <Switch
          checked={settings.enabled}
          onChange={(event) => onToggle(event.currentTarget.checked)}
          disabled={busy}
          label={settings.enabled ? t('agents.on') : t('agents.off')}
        />
      </Group>
      <Divider my="md" />
      <Group justify="space-between" align="flex-start" gap="md">
        <Stack gap={2}>
          <Text fw={500}>{t('agents.requireAdminMode')}</Text>
          <Text size="xs" c="dimmed">
            {t('agents.requireAdminModeText')}
          </Text>
        </Stack>
        <Switch
          checked={settings.requireAdminMode}
          onChange={(event) => onRequireAdminMode(event.currentTarget.checked)}
          disabled={busy}
          aria-label={t('agents.requireAdminMode')}
        />
      </Group>
      <Divider my="md" />
      <Group gap="xl" align="flex-start">
        <ModeRow
          title={t('agents.diagnostics')}
          window={settings.diagnostics}
          warning={t('agents.diagnosticsText')}
          onEnable={() => onMode('diagnostics')}
          onDisable={() => onDisableMode('diagnostics')}
        />
        <ModeRow
          title={t('agents.admin')}
          window={settings.admin}
          warning={t('agents.adminText')}
          onEnable={() => onMode('admin')}
          onDisable={() => onDisableMode('admin')}
        />
      </Group>
    </Card>
  );
}

function ModeRow({
  title,
  window,
  warning,
  onEnable,
  onDisable,
}: {
  title: string;
  window: AgentSettingsView['diagnostics'];
  warning: string;
  onEnable: () => void;
  onDisable: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap={4} style={{ flex: 1, minWidth: 240 }}>
      <Group gap="xs">
        <IconShieldCheck size={16} />
        <Text fw={500}>{title}</Text>
        <Badge color={window.enabled ? 'yellow' : 'gray'} variant="light">
          {window.enabled
            ? t('agents.modeOn', {
                at: window.expiresAt ? dayjs(window.expiresAt).format('HH:mm') : '—',
              })
            : t('agents.off')}
        </Badge>
      </Group>
      <Text size="xs" c="dimmed">
        {warning}
      </Text>
      {window.enabled ? (
        <Group gap="xs">
          <Button size="compact-xs" variant="default" w="fit-content" onClick={onDisable}>
            {t('agents.turnOff')}
          </Button>
          {window.reason && (
            <Text size="xs" c="dimmed">
              {window.reason}
            </Text>
          )}
        </Group>
      ) : (
        <Button size="compact-xs" variant="light" color="yellow" w="fit-content" onClick={onEnable}>
          {t('agents.turnOn')}
        </Button>
      )}
    </Stack>
  );
}
