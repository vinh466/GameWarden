import { useTranslation } from 'react-i18next';
import { Badge, Group, Switch } from '@mantine/core';
import type { AgentHostView } from '@/api/generated';

/** The Agent's live badges and its one switch: what is connected, what still needs a secret. */
export function AgentStatus({
  view,
  enabled,
  onEnabledChange,
}: {
  view: AgentHostView;
  enabled: boolean;
  onEnabledChange: (enabled: boolean) => void;
}) {
  const { t } = useTranslation();

  return (
    <Group justify="space-between" align="center" gap="sm" wrap="wrap">
      <Group gap="xs">
        <Badge color={view.enabled ? 'green' : 'gray'} variant="light">
          {view.enabled ? t('system.operations.enabled') : t('system.operations.disabled')}
        </Badge>
        <Badge color={view.botConnected ? 'green' : 'gray'} variant="light">
          {view.botConnected
            ? t('system.operations.connected')
            : t('system.operations.disconnected')}
        </Badge>
        <Badge color={view.botTokenSet ? 'green' : 'yellow'} variant="light">
          {view.botTokenSet ? t('system.operations.tokenSet') : t('system.operations.tokenMissing')}
        </Badge>
        <Badge color={view.modelKeySet ? 'green' : 'yellow'} variant="light">
          {view.modelKeySet
            ? t('system.operations.modelKeySet')
            : t('system.operations.modelKeyMissing')}
        </Badge>
      </Group>
      <Switch
        label={t('system.operations.enabledSwitch')}
        checked={enabled}
        onChange={(event) => onEnabledChange(event.currentTarget.checked)}
      />
    </Group>
  );
}
