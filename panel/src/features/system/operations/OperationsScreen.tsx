import { useEffect, useState } from 'react';
import { IconBrandDiscord, IconKey, IconRobot } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
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
  Title,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import {
  setAgentHostSecret,
  updateAgentHost,
  type AgentDiscordSettings,
  type AgentHostSettingsDocument,
  type AgentModelSettings,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';

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
  const instancesList = instances.data ?? [];
  const update = (patch: Partial<AgentHostSettingsDocument>) =>
    setSettings((current) => ({ ...(current ?? {}), ...patch }));
  const updateModel = (patch: Partial<AgentModelSettings>) =>
    update({ model: { ...model, ...patch } });
  const updateDiscord = (patch: Partial<AgentDiscordSettings>) =>
    update({ discord: { ...discord, ...patch } });
  const parseIds = (value: string) =>
    value
      .split(/[\s,]+/)
      .map((item) => item.trim())
      .filter((item) => /^\d+$/.test(item));
  const saveReady = () => save.mutate(settings);

  return (
    <SystemTab
      description={t('system.operations.description')}
      actions={
        <Button loading={save.isPending} onClick={saveReady}>
          {t('actions.save')}
        </Button>
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
                  const value = Number(event.currentTarget.value.replace(/\D/g, ''));
                  if (value > 0) {
                    next[row.id] = String(value);
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
