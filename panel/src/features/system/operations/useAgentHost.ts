import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notifications } from '@mantine/notifications';
import {
  restartCore,
  setAgentHostSecret,
  testAgentHostDiscord,
  testAgentHostModel,
  updateAgentHost,
  type AgentDiscordSettings,
  type AgentHostSettingsDocument,
  type AgentModelSettings,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import type { TestState } from './types';

/**
 * The Operations screen's data: the saved settings and the draft the Owner edits, the save, secret, restart and test
 * calls, and the badges they refresh. Kept out of the screen so the layout stays readable (docs/11 §10 rule 5).
 */
export function useAgentHost() {
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

  const model = settings?.model ?? {};
  const discord = settings?.discord ?? {};
  const autonomy = settings?.autonomy ?? {};
  const dirty =
    settings !== null && JSON.stringify(settings) !== JSON.stringify(host.data?.settings ?? {});

  const update = (patch: Partial<AgentHostSettingsDocument>) =>
    setSettings((current) => ({ ...(current ?? {}), ...patch }));
  const updateModel = (patch: Partial<AgentModelSettings>) =>
    update({ model: { ...model, ...patch } });
  const updateDiscord = (patch: Partial<AgentDiscordSettings>) =>
    update({ discord: { ...discord, ...patch } });
  const setHeaders = (next: Record<string, string>) => updateModel({ headers: next });

  return {
    host,
    settings,
    instances: instances.data ?? [],
    model,
    discord,
    autonomy,
    dirty,
    botToken,
    setBotToken,
    modelKey,
    setModelKey,
    testChannelId,
    setTestChannelId,
    testResult,
    update,
    updateModel,
    updateDiscord,
    setHeaders,
    save: () => {
      if (settings) {
        save.mutate(settings);
      }
    },
    savePending: save.isPending,
    saveSecret: (name: string, value: string) => secret.mutate({ name, value }),
    savingSecret: secret.isPending ? (secret.variables?.name ?? null) : null,
    restart: () => restart.mutate(),
    restartPending: restart.isPending,
    testModel,
    testDiscord,
  };
}
