import { useEffect, useState } from 'react';
import {
  IconBrandDiscord,
  IconCheck,
  IconKey,
  IconPlus,
  IconRefresh,
  IconRobot,
  IconTrash,
  IconX,
} from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Card,
  Divider,
  Group,
  NumberInput,
  PasswordInput,
  Select,
  SimpleGrid,
  Skeleton,
  Stack,
  Switch,
  Text,
  TextInput,
  ThemeIcon,
  Title,
} from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import {
  restartCore,
  setAgentHostSecret,
  testAgentHostDiscord,
  testAgentHostModel,
  updateAgentHost,
  type AgentDiscordTestResult,
  type AgentModelTestResult,
  type AgentDiscordSettings,
  type AgentHostSettingsDocument,
  type AgentModelSettings,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';

type TestState =
  | { kind: 'model'; result: AgentModelTestResult }
  | { kind: 'discord'; result: AgentDiscordTestResult }
  | { kind: 'model' | 'discord'; error: string };

/**
 * System → Operations (docs/20): the Owner's in-Core Agent. Discord is the daily interface; this screen is the
 * place to connect the bot, choose the model and see whether it is ready.
 */
export function OperationsScreen() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const host = useQuery(queries.agentHost());
  const instances = useQuery(queries.instances());
  const [settings, setSettings] = useState<AgentHostSettingsDocument | null>(null);
  const [botToken, setBotToken] = useState('');
  const [modelKey, setModelKey] = useState('');
  const [testChannelId, setTestChannelId] = useState('');
  const [testResult, setTestResult] = useState<TestState | null>(null);

  useEffect(() => {
    if (host.data) {
      setSettings(host.data.settings);
    }
  }, [host.data]);

  const save = useMutation({
    mutationFn: (body: AgentHostSettingsDocument) => updateAgentHost({ body, throwOnError: true }),
    onSuccess: ({ data }) => {
      queryClient.setQueryData(queries.agentHost().queryKey, data);
      setSettings(data.settings);
      notifications.show({ color: 'green', message: t('system.operations.saved') });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  // Test only what is already saved; the Owner presses Save first (the hint says so).
  const testModel = useMutation({
    mutationFn: () => testAgentHostModel({ throwOnError: true }),
    onSuccess: ({ data }) => setTestResult({ kind: 'model', result: data }),
    onError: (error) =>
      setTestResult({ kind: 'model', error: toProblem(error, t('errors.generic')).title }),
  });

  const testDiscord = useMutation({
    mutationFn: (channelId: string) =>
      testAgentHostDiscord({ body: { channelId }, throwOnError: true }),
    onSuccess: ({ data }) => setTestResult({ kind: 'discord', result: data }),
    onError: (error) =>
      setTestResult({ kind: 'discord', error: toProblem(error, t('errors.generic')).title }),
  });

  const restart = useMutation({
    mutationFn: () => restartCore({ throwOnError: true }),
    onSuccess: () =>
      notifications.show({
        color: 'blue',
        message: t('system.operations.restartStarted'),
        autoClose: 10_000,
      }),
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const secret = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      setAgentHostSecret({ path: { name }, body: { value }, throwOnError: true }),
    onSuccess: ({ data }, variables) => {
      queryClient.setQueryData(queries.agentHost().queryKey, data);
      if (variables.name === 'discord-bot-token') {
        setBotToken('');
      } else {
        setModelKey('');
      }
      notifications.show({ color: 'green', message: t('system.operations.secretSaved') });
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  if (host.isError) {
    return (
      <SystemTab description={t('system.operations.description')}>
        <ProblemAlert error={host.error} onRetry={() => void host.refetch()} />
      </SystemTab>
    );
  }

  if (!host.data || !settings) {
    return (
      <SystemTab description={t('system.operations.description')}>
        <Skeleton h={320} />
      </SystemTab>
    );
  }

  const view = host.data;
  const model = settings.model ?? {};
  const discord = settings.discord ?? {};
  const autonomy = settings.autonomy ?? {};
  const channels = discord.channels ?? {};
  const headers = model.headers ?? {};
  const channelIds = Object.values(channels);
  const selectedTestChannel =
    testChannelId && channelIds.includes(testChannelId)
      ? testChannelId
      : (channels.system ?? channelIds[0] ?? '');
  const dirty = JSON.stringify(settings) !== JSON.stringify(host.data.settings);
  const instancesList = instances.data ?? [];
  const update = (patch: Partial<AgentHostSettingsDocument>) =>
    setSettings((current) => ({ ...(current ?? {}), ...patch }));
  const updateModel = (patch: Partial<AgentModelSettings>) =>
    update({ model: { ...model, ...patch } });
  const updateDiscord = (patch: Partial<AgentDiscordSettings>) =>
    update({ discord: { ...discord, ...patch } });
  const setHeaders = (next: Record<string, string>) => updateModel({ headers: next });
  const parseIds = (value: string) =>
    value
      .split(/[\s,]+/)
      .map((item) => item.trim())
      .filter((item) => /^\d+$/.test(item));
  const saveReady = () => save.mutate(settings);
  const confirmRestart = () =>
    modals.openConfirmModal({
      title: t('system.operations.restartTitle'),
      children: (
        <Stack gap="sm">
          <Text size="sm">{t('system.operations.restartText')}</Text>
          {JSON.stringify(settings) !== JSON.stringify(host.data.settings) && (
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
      onConfirm: () => restart.mutate(),
    });

  return (
    <SystemTab
      description={t('system.operations.description')}
      actions={
        <Group gap="xs">
          <Button
            variant="default"
            leftSection={<IconRefresh size={16} />}
            loading={restart.isPending}
            onClick={confirmRestart}
          >
            {t('system.operations.restartCore')}
          </Button>
          <Button loading={save.isPending} onClick={saveReady}>
            {t('actions.save')}
          </Button>
        </Group>
      }
    >
      <Stack gap="lg">
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
              {view.botTokenSet
                ? t('system.operations.tokenSet')
                : t('system.operations.tokenMissing')}
            </Badge>
            <Badge color={view.modelKeySet ? 'green' : 'yellow'} variant="light">
              {view.modelKeySet
                ? t('system.operations.modelKeySet')
                : t('system.operations.modelKeyMissing')}
            </Badge>
          </Group>
          <Switch
            label={t('system.operations.enabledSwitch')}
            checked={settings.enabled ?? false}
            onChange={(event) => update({ enabled: event.currentTarget.checked })}
          />
        </Group>

        {view.lastError && (
          <Alert color="red" title={t('system.operations.lastError')}>
            {view.lastError}
          </Alert>
        )}

        {dirty && <Alert color="yellow">{t('system.operations.unsavedTestHint')}</Alert>}

        <Alert color="blue" icon={<IconRobot size={18} />}>
          {t('system.operations.restartHint')}
        </Alert>

        <Card>
          <Group gap="xs" mb="sm">
            <IconBrandDiscord size={20} />
            <Title order={5}>{t('system.operations.discord')}</Title>
          </Group>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
            <TextInput
              label={t('system.operations.guildId')}
              description={t('system.operations.guildHint')}
              value={discord.guildId ?? ''}
              onChange={(event) =>
                updateDiscord({ guildId: event.currentTarget.value.replace(/\D/g, '') })
              }
              inputMode="numeric"
            />
            <Switch
              label={t('system.operations.requireMention')}
              description={t('system.operations.requireMentionHint')}
              checked={discord.requireMention ?? true}
              onChange={(event) => updateDiscord({ requireMention: event.currentTarget.checked })}
              mt="md"
            />
            <TextInput
              label={t('system.operations.allowedUsers')}
              description={t('system.operations.allowedUsersHint')}
              value={(discord.allowedUserIds ?? []).join(', ')}
              onChange={(event) =>
                updateDiscord({ allowedUserIds: parseIds(event.currentTarget.value) })
              }
              placeholder="123456789012345678"
            />
            <TextInput
              label={t('system.operations.allowedRoles')}
              description={t('system.operations.allowedRolesHint')}
              value={(discord.allowedRoleIds ?? []).join(', ')}
              onChange={(event) =>
                updateDiscord({ allowedRoleIds: parseIds(event.currentTarget.value) })
              }
              placeholder="123456789012345678"
            />
            <Select
              label={t('system.operations.language')}
              value={discord.language ?? 'vi'}
              onChange={(language) => updateDiscord({ language: language ?? 'vi' })}
              data={[
                { value: 'vi', label: t('system.operations.languageVi') },
                { value: 'en', label: t('system.operations.languageEn') },
              ]}
            />
          </SimpleGrid>
          <Divider my="md" />
          <Title order={6} mb="xs">
            {t('system.operations.channels')}
          </Title>
          <Text size="xs" c="dimmed" mb="sm">
            {t('system.operations.channelsHint')}
          </Text>
          <Stack gap="xs">
            {[
              { id: 'system', label: t('system.operations.systemChannel') },
              ...instancesList.map((instance) => ({
                id: instance.id,
                label: `${instance.displayName} · ${instance.id}`,
              })),
            ].map((row) => (
              <TextInput
                key={row.id}
                label={row.label}
                value={channels[row.id] ?? ''}
                onChange={(event) => {
                  const next = { ...channels };
                  // Keep the snowflake as a string: Number() would round it above 2^53.
                  const value = event.currentTarget.value.replace(/\D/g, '');
                  if (value.length > 0) {
                    next[row.id] = value;
                  } else {
                    delete next[row.id];
                  }
                  updateDiscord({ channels: next });
                }}
                placeholder={t('system.operations.channelPlaceholder')}
                inputMode="numeric"
              />
            ))}
          </Stack>
          <Group mt="md" gap="xs" align="flex-end">
            <PasswordInput
              label={t('system.operations.botToken')}
              description={t('system.operations.botTokenHint')}
              placeholder={
                view.botTokenSet
                  ? t('system.operations.secretSet')
                  : t('system.operations.secretNotSet')
              }
              value={botToken}
              onChange={(event) => setBotToken(event.currentTarget.value)}
              leftSection={<IconKey size={16} />}
              flex={1}
            />
            <Button
              variant="light"
              loading={secret.isPending && secret.variables?.name === 'discord-bot-token'}
              disabled={!botToken.trim()}
              onClick={() => secret.mutate({ name: 'discord-bot-token', value: botToken })}
            >
              {t('system.operations.saveToken')}
            </Button>
          </Group>
          <Group mt="md" justify="space-between" align="flex-end" gap="sm" wrap="wrap">
            <Select
              label={t('system.operations.testChannel')}
              description={t('system.operations.testChannelHint')}
              data={Object.entries(channels).map(([key, id]) => ({
                value: id,
                label: `${key === 'system' ? t('system.operations.systemChannel') : key} · ${id}`,
              }))}
              value={selectedTestChannel || null}
              onChange={(value) => setTestChannelId(value ?? '')}
              w={{ base: '100%', sm: 360 }}
            />
            <Button
              variant="default"
              loading={testDiscord.isPending}
              disabled={!view.botTokenSet || !selectedTestChannel}
              onClick={() => testDiscord.mutate(selectedTestChannel)}
            >
              {t('system.operations.testDiscord')}
            </Button>
          </Group>
          <Text size="xs" c="dimmed" mt={4}>
            {view.botTokenSet && selectedTestChannel
              ? t('system.operations.testDiscordHint')
              : t('system.operations.testDiscordMissing')}
          </Text>
          {testResult?.kind === 'discord' && <TestChecklist state={testResult} />}
        </Card>

        <Card>
          <Group gap="xs" mb="sm">
            <IconRobot size={20} />
            <Title order={5}>{t('system.operations.model')}</Title>
          </Group>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
            <TextInput
              label={t('system.operations.baseUrl')}
              description={t('system.operations.baseUrlHint')}
              value={model.baseUrl ?? ''}
              onChange={(event) => updateModel({ baseUrl: event.currentTarget.value })}
              placeholder="https://model.example/v1"
            />
            <TextInput
              label={t('system.operations.modelId')}
              value={model.model ?? ''}
              onChange={(event) => updateModel({ model: event.currentTarget.value })}
              placeholder="deepseek-v4.1-flash"
            />
            <NumberInput
              label={t('system.operations.maxTokens')}
              value={model.maxTokens ?? 4096}
              onChange={(value) => updateModel({ maxTokens: Number(value) || 4096 })}
              min={256}
              max={32768}
            />
            <NumberInput
              label={t('system.operations.maxToolCalls')}
              value={model.maxToolCalls ?? 12}
              onChange={(value) => updateModel({ maxToolCalls: Number(value) || 12 })}
              min={1}
              max={32}
            />
            <NumberInput
              label={t('system.operations.timeout')}
              value={model.timeoutSeconds ?? 120}
              onChange={(value) => updateModel({ timeoutSeconds: Number(value) || 120 })}
              min={10}
              max={600}
            />
            <NumberInput
              label={t('system.operations.temperature')}
              value={model.temperature ?? 0.2}
              onChange={(value) => updateModel({ temperature: Number(value) || 0.2 })}
              min={0}
              max={2}
              step={0.1}
            />
          </SimpleGrid>
          <Divider my="md" />
          <Group justify="space-between" align="center" mb="xs">
            <Title order={6}>{t('system.operations.customHeaders')}</Title>
            <Button
              size="xs"
              variant="light"
              leftSection={<IconPlus size={14} />}
              onClick={() =>
                setHeaders({ ...headers, [`X-Custom-${Object.keys(headers).length + 1}`]: '' })
              }
            >
              {t('system.operations.addHeader')}
            </Button>
          </Group>
          <Text size="xs" c="dimmed" mb="sm">
            {t('system.operations.customHeadersHint')}
          </Text>
          <Stack gap="xs">
            {Object.entries(headers).map(([name, value]) => (
              <Group key={name} align="flex-end" gap="xs" wrap="nowrap">
                <TextInput
                  label={t('system.operations.headerName')}
                  defaultValue={name}
                  onBlur={(event) => {
                    const nextName = event.currentTarget.value.trim();
                    if (!nextName || nextName === name) {
                      event.currentTarget.value = name;
                      return;
                    }

                    const next = { ...headers };
                    const current = next[name] ?? '';
                    delete next[name];
                    next[nextName] = current;
                    setHeaders(next);
                  }}
                  flex={1}
                />
                <TextInput
                  label={t('system.operations.headerValue')}
                  value={value}
                  onChange={(event) =>
                    setHeaders({ ...headers, [name]: event.currentTarget.value })
                  }
                  flex={2}
                />
                <ActionIcon
                  variant="subtle"
                  color="red"
                  aria-label={t('system.operations.removeHeader')}
                  onClick={() => {
                    const next = { ...headers };
                    delete next[name];
                    setHeaders(next);
                  }}
                >
                  <IconTrash size={16} />
                </ActionIcon>
              </Group>
            ))}
          </Stack>
          <Group mt="md" gap="xs" align="flex-end">
            <PasswordInput
              label={t('system.operations.modelKey')}
              description={t('system.operations.modelKeyHint')}
              placeholder={
                view.modelKeySet
                  ? t('system.operations.secretSet')
                  : t('system.operations.secretNotSet')
              }
              value={modelKey}
              onChange={(event) => setModelKey(event.currentTarget.value)}
              leftSection={<IconKey size={16} />}
              flex={1}
            />
            <Button
              variant="light"
              loading={secret.isPending && secret.variables?.name === 'model-api-key'}
              disabled={!modelKey.trim()}
              onClick={() => secret.mutate({ name: 'model-api-key', value: modelKey })}
            >
              {t('system.operations.saveKey')}
            </Button>
          </Group>
          <Group mt="md" justify="space-between" align="center">
            <Text size="xs" c="dimmed">
              {view.modelKeySet
                ? t('system.operations.testModelHint')
                : t('system.operations.testModelMissing')}
            </Text>
            <Button
              variant="default"
              loading={testModel.isPending}
              disabled={!view.modelKeySet || !model.baseUrl || !model.model}
              onClick={() => testModel.mutate()}
            >
              {t('system.operations.testModel')}
            </Button>
          </Group>
          {testResult?.kind === 'model' && <TestChecklist state={testResult} />}
        </Card>

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
                update({
                  autonomy: {
                    ...autonomy,
                    approvalTimeoutMinutes: Number(value) || 15,
                  },
                })
              }
              min={1}
              max={1440}
            />
            <NumberInput
              label={t('system.operations.maxWrites')}
              value={autonomy.maxWritesPerHour ?? 6}
              onChange={(value) =>
                update({ autonomy: { ...autonomy, maxWritesPerHour: Number(value) || 6 } })
              }
              min={1}
              max={100}
            />
          </SimpleGrid>
        </Card>

        <Group justify="flex-end">
          <Button loading={save.isPending} onClick={saveReady}>
            {t('actions.save')}
          </Button>
        </Group>
      </Stack>
    </SystemTab>
  );
}

function TestChecklist({ state }: { state: TestState }) {
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
