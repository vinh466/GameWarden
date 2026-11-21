import { IconRefresh, IconRobot } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Group, Skeleton, Stack, Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';
import { AgentObservabilityCard } from './AgentObservabilityCard';
import { AgentQualityCard } from './AgentQualityCard';
import { AgentStatus } from './AgentStatus';
import { ApprovalsCard } from './ApprovalsCard';
import { DiscordSettingsCard } from './DiscordSettingsCard';
import { IncidentsCard } from './IncidentsCard';
import { ModelSettingsCard } from './ModelSettingsCard';
import { TraceExplorerCard } from './TraceExplorerCard';
import { useAgentHost } from './useAgentHost';

/**
 * System → Operations (docs/20): the Owner's in-Core Agent. Discord is the daily interface; this screen is the
 * place to connect the bot, choose the model and see whether it is ready.
 */
export function OperationsScreen() {
  const { t } = useTranslation();
  const agent = useAgentHost();

  if (agent.host.isError) {
    return (
      <SystemTab description={t('system.operations.description')}>
        <ProblemAlert error={agent.host.error} onRetry={() => void agent.host.refetch()} />
      </SystemTab>
    );
  }

  if (!agent.host.data || !agent.settings) {
    return (
      <SystemTab description={t('system.operations.description')}>
        <Skeleton h={320} />
      </SystemTab>
    );
  }

  const view = agent.host.data;
  const { settings, dirty } = agent;

  const confirmRestart = () =>
    modals.openConfirmModal({
      title: t('system.operations.restartTitle'),
      children: (
        <Stack gap="sm">
          <Text size="sm">{t('system.operations.restartText')}</Text>
          {dirty && (
            <Text size="sm" c="yellow">
              {t('system.operations.restartDirty')}
            </Text>
          )}
        </Stack>
      ),
      labels: {
        confirm: t('system.operations.restartConfirm'),
        cancel: t('actions.cancel'),
      },
      confirmProps: { color: 'red' },
      onConfirm: agent.restart,
    });

  return (
    <SystemTab
      description={t('system.operations.description')}
      actions={
        <Group gap="xs">
          <Button
            variant="default"
            leftSection={<IconRefresh size={16} />}
            loading={agent.restartPending}
            onClick={confirmRestart}
          >
            {t('system.operations.restartCore')}
          </Button>
          <Button loading={agent.savePending} onClick={agent.save}>
            {t('actions.save')}
          </Button>
        </Group>
      }
    >
      <Stack gap="lg">
        <AgentStatus
          view={view}
          enabled={settings.enabled ?? false}
          onEnabledChange={(enabled) => agent.update({ enabled })}
        />

        {view.lastError && (
          <Alert color="red" title={t('system.operations.lastError')}>
            {view.lastError}
          </Alert>
        )}

        {dirty && <Alert color="yellow">{t('system.operations.unsavedTestHint')}</Alert>}

        <Alert color="blue" icon={<IconRobot size={18} />}>
          {t('system.operations.restartHint')}
        </Alert>

        <DiscordSettingsCard
          botTokenSet={view.botTokenSet}
          discord={agent.discord}
          instances={agent.instances}
          botToken={agent.botToken}
          onBotTokenChange={agent.setBotToken}
          testChannelId={agent.testChannelId}
          onTestChannelChange={agent.setTestChannelId}
          testResult={agent.testResult}
          onDiscordChange={agent.updateDiscord}
          onSaveSecret={agent.saveSecret}
          savingSecret={agent.savingSecret}
          onTest={(channelId) => agent.testDiscord.mutate(channelId)}
          testing={agent.testDiscord.isPending}
        />

        <ModelSettingsCard
          modelKeySet={view.modelKeySet}
          model={agent.model}
          modelKey={agent.modelKey}
          onModelKeyChange={agent.setModelKey}
          testResult={agent.testResult}
          onModelChange={agent.updateModel}
          onHeadersChange={agent.setHeaders}
          onSaveSecret={agent.saveSecret}
          savingSecret={agent.savingSecret}
          onTest={() => agent.testModel.mutate()}
          testing={agent.testModel.isPending}
        />

        <ApprovalsCard
          autonomy={agent.autonomy}
          onChange={(next) => agent.update({ autonomy: next })}
        />

        <AgentObservabilityCard />

        <IncidentsCard />

        <TraceExplorerCard />

        <AgentQualityCard />

        <Group justify="flex-end">
          <Button loading={agent.savePending} onClick={agent.save}>
            {t('actions.save')}
          </Button>
        </Group>
      </Stack>
    </SystemTab>
  );
}
