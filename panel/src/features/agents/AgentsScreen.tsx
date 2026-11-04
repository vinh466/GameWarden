import { useState } from 'react';
import { IconAlertTriangle, IconPlus } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Group, Modal, Select, Skeleton, Stack, TextInput } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import {
  updateAgentClient,
  updateAgentSettings,
  type AgentClientView,
  type AgentTokenCreated,
  type UpdateAgentSettingsRequest,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';
import { AgentAuditCard } from './AgentAuditCard';
import { AgentClientsCard } from './AgentClientsCard';
import { AgentFeatureCard } from './AgentFeatureCard';
import { ClientTokensModal } from './ClientTokensModal';
import { CreateAgentModal } from './CreateAgentModal';
import { TokenReveal } from './TokenReveal';

/** Agent access: the feature switch, clients, tokens and audit, all on the System screen (docs/15). */
export function AgentsScreen() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const settings = useQuery(queries.agentSettings());
  const catalog = useQuery(queries.agentPermissions());
  const clients = useQuery(queries.agents());
  const audit = useQuery(queries.agentAudit());
  const [createOpen, create] = useDisclosure(false);
  const [token, setToken] = useState<AgentTokenCreated | null>(null);
  const [tokenClient, setTokenClient] = useState<AgentClientView | null>(null);
  const [mode, setMode] = useState<'diagnostics' | 'admin' | null>(null);
  const [modeReason, setModeReason] = useState('debug session');
  const [modeMinutes, setModeMinutes] = useState('30');

  const refresh = () => void queryClient.invalidateQueries();
  const fail = (error: unknown) =>
    notifications.show({
      color: 'red',
      title: t('feedback.failed'),
      message: toProblem(error, t('errors.generic')).title,
    });

  const settingsMutation = useMutation({
    mutationFn: (body: UpdateAgentSettingsRequest) =>
      updateAgentSettings({ body, throwOnError: true }),
    onSuccess: refresh,
    onError: fail,
  });

  const clientMutation = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
      updateAgentClient({
        path: { id },
        body: {
          name: null,
          description: null,
          enabled,
          permissions: null,
          trustedFull: null,
          allowedNetworks: null,
        },
        throwOnError: true,
      }),
    onSuccess: () => {
      refresh();
      notifications.show({ color: 'green', message: t('agents.clientUpdated') });
    },
    onError: fail,
  });

  const retry = () => {
    void settings.refetch();
    void catalog.refetch();
    void clients.refetch();
    void audit.refetch();
  };

  if (settings.isError || catalog.isError || clients.isError) {
    return (
      <SystemTab description={t('agents.description')}>
        <ProblemAlert error={settings.error ?? catalog.error ?? clients.error} onRetry={retry} />
      </SystemTab>
    );
  }

  if (!settings.data || !catalog.data || !clients.data) {
    return (
      <SystemTab description={t('agents.description')}>
        <Stack gap="lg">
          <Skeleton h={180} />
          <Skeleton h={240} />
        </Stack>
      </SystemTab>
    );
  }

  const current = settings.data;
  const setEnabled = (enabled: boolean) =>
    settingsMutation.mutate(
      {
        enabled,
        requireAdminMode: null,
        diagnostics: null,
        admin: null,
        allowedNetworks: null,
      },
      {
        onSuccess: () =>
          notifications.show({
            color: 'green',
            message: enabled ? t('agents.featureEnabled') : t('agents.featureDisabled'),
          }),
      }
    );

  const setRequireAdminMode = (requireAdminMode: boolean) =>
    settingsMutation.mutate(
      {
        enabled: null,
        requireAdminMode,
        diagnostics: null,
        admin: null,
        allowedNetworks: null,
      },
      {
        onSuccess: () =>
          notifications.show({
            color: 'green',
            message: requireAdminMode
              ? t('agents.adminModeRequired')
              : t('agents.adminModeNotRequired'),
          }),
      }
    );

  const openMode = (next: 'diagnostics' | 'admin') => {
    setMode(next);
    setModeReason(next === 'diagnostics' ? 'debug session' : 'maintenance');
    setModeMinutes(next === 'diagnostics' ? '30' : '15');
  };

  const enableMode = () => {
    if (!mode) {
      return;
    }

    settingsMutation.mutate(
      {
        enabled: null,
        requireAdminMode: null,
        diagnostics:
          mode === 'diagnostics'
            ? { enabled: true, ttlMinutes: Number(modeMinutes), reason: modeReason }
            : null,
        admin:
          mode === 'admin'
            ? { enabled: true, ttlMinutes: Number(modeMinutes), reason: modeReason }
            : null,
        allowedNetworks: null,
      },
      {
        onSuccess: () => {
          setMode(null);
          notifications.show({ color: 'green', message: t('agents.modeEnabled') });
        },
      }
    );
  };

  const disableMode = (which: 'diagnostics' | 'admin') =>
    settingsMutation.mutate(
      {
        enabled: null,
        requireAdminMode: null,
        diagnostics:
          which === 'diagnostics' ? { enabled: false, ttlMinutes: null, reason: null } : null,
        admin: which === 'admin' ? { enabled: false, ttlMinutes: null, reason: null } : null,
        allowedNetworks: null,
      },
      {
        onSuccess: () => notifications.show({ color: 'green', message: t('agents.modeDisabled') }),
      }
    );

  return (
    <SystemTab
      description={t('agents.description')}
      actions={
        <Button leftSection={<IconPlus size={16} />} onClick={create.open}>
          {t('agents.create')}
        </Button>
      }
    >
      <Stack gap="lg">
        <AgentFeatureCard
          settings={current}
          busy={settingsMutation.isPending}
          onToggle={setEnabled}
          onRequireAdminMode={setRequireAdminMode}
          onMode={openMode}
          onDisableMode={disableMode}
        />
        <AgentClientsCard
          clients={clients.data}
          busyId={clientMutation.isPending ? clientMutation.variables?.id : undefined}
          onToggle={(client) => clientMutation.mutate({ id: client.id, enabled: !client.enabled })}
          onTokens={setTokenClient}
        />
        {audit.isError ? (
          <ProblemAlert error={audit.error} onRetry={() => void audit.refetch()} />
        ) : (
          <AgentAuditCard rows={audit.data ?? []} />
        )}
      </Stack>

      <CreateAgentModal
        opened={createOpen}
        onClose={create.close}
        permissions={catalog.data.permissions}
        presets={catalog.data.presets}
        onCreated={(_created, createdToken) => {
          create.close();
          refresh();
          setToken(createdToken);
          setTokenClient(null);
        }}
      />

      <Modal
        opened={token !== null}
        onClose={() => setToken(null)}
        title={t('agents.tokenTitle')}
        size="lg"
      >
        {token && <TokenReveal token={token} />}
      </Modal>

      <ClientTokensModal
        client={tokenClient}
        onClose={() => setTokenClient(null)}
        onRefresh={refresh}
      />

      <Modal opened={mode !== null} onClose={() => setMode(null)} title={t('agents.modeTitle')}>
        <Stack>
          <Alert color="yellow" icon={<IconAlertTriangle size={18} />}>
            {mode === 'diagnostics' ? t('agents.diagnosticsWarning') : t('agents.adminWarning')}
          </Alert>
          <TextInput
            label={t('agents.reason')}
            value={modeReason}
            onChange={(event) => setModeReason(event.currentTarget.value)}
          />
          <Select
            label={t('agents.durationMinutes')}
            value={modeMinutes}
            onChange={(value) => setModeMinutes(value ?? '30')}
            data={mode === 'admin' ? ['15', '30', '60'] : ['15', '30', '60', '120', '240', '480']}
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setMode(null)}>
              {t('actions.cancel')}
            </Button>
            <Button color="yellow" onClick={enableMode} loading={settingsMutation.isPending}>
              {t('agents.enableMode')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </SystemTab>
  );
}
