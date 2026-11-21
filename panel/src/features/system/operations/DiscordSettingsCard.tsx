import { IconBrandDiscord, IconKey } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  Divider,
  Group,
  PasswordInput,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import type { AgentDiscordSettings, InstanceSnapshot } from '@/api/generated';
import { TestChecklist } from './TestChecklist';
import type { TestState } from './types';

/** Discord is the Agent's daily interface: guild, who may talk to it, the channel per server and the bot token. */
export function DiscordSettingsCard({
  botTokenSet,
  discord,
  instances,
  botToken,
  onBotTokenChange,
  testChannelId,
  onTestChannelChange,
  testResult,
  onDiscordChange,
  onSaveSecret,
  savingSecret,
  onTest,
  testing,
}: {
  botTokenSet: boolean;
  discord: AgentDiscordSettings;
  instances: InstanceSnapshot[];
  botToken: string;
  onBotTokenChange: (value: string) => void;
  testChannelId: string;
  onTestChannelChange: (value: string) => void;
  testResult: TestState | null;
  onDiscordChange: (patch: Partial<AgentDiscordSettings>) => void;
  onSaveSecret: (name: string, value: string) => void;
  savingSecret: string | null;
  onTest: (channelId: string) => void;
  testing: boolean;
}) {
  const { t } = useTranslation();
  const channels = discord.channels ?? {};
  const channelIds = Object.values(channels);
  const selectedTestChannel =
    testChannelId && channelIds.includes(testChannelId)
      ? testChannelId
      : (channels.system ?? channelIds[0] ?? '');

  const parseIds = (value: string) =>
    value
      .split(/[\s,]+/)
      .map((item) => item.trim())
      .filter((item) => /^\d+$/.test(item));

  return (
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
            onDiscordChange({ guildId: event.currentTarget.value.replace(/\D/g, '') })
          }
          inputMode="numeric"
        />
        <Switch
          label={t('system.operations.requireMention')}
          description={t('system.operations.requireMentionHint')}
          checked={discord.requireMention ?? true}
          onChange={(event) => onDiscordChange({ requireMention: event.currentTarget.checked })}
          mt="md"
        />
        <TextInput
          label={t('system.operations.allowedUsers')}
          description={t('system.operations.allowedUsersHint')}
          value={(discord.allowedUserIds ?? []).join(', ')}
          onChange={(event) =>
            onDiscordChange({ allowedUserIds: parseIds(event.currentTarget.value) })
          }
          placeholder="123456789012345678"
        />
        <TextInput
          label={t('system.operations.allowedRoles')}
          description={t('system.operations.allowedRolesHint')}
          value={(discord.allowedRoleIds ?? []).join(', ')}
          onChange={(event) =>
            onDiscordChange({ allowedRoleIds: parseIds(event.currentTarget.value) })
          }
          placeholder="123456789012345678"
        />
        <Select
          label={t('system.operations.language')}
          value={discord.language ?? 'vi'}
          onChange={(language) => onDiscordChange({ language: language ?? 'vi' })}
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
          ...instances.map((instance) => ({
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
              onDiscordChange({ channels: next });
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
            botTokenSet ? t('system.operations.secretSet') : t('system.operations.secretNotSet')
          }
          value={botToken}
          onChange={(event) => onBotTokenChange(event.currentTarget.value)}
          leftSection={<IconKey size={16} />}
          flex={1}
        />
        <Button
          variant="light"
          loading={savingSecret === 'discord-bot-token'}
          disabled={!botToken.trim()}
          onClick={() => onSaveSecret('discord-bot-token', botToken)}
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
          onChange={(value) => onTestChannelChange(value ?? '')}
          w={{ base: '100%', sm: 360 }}
        />
        <Button
          variant="default"
          loading={testing}
          disabled={!botTokenSet || !selectedTestChannel}
          onClick={() => onTest(selectedTestChannel)}
        >
          {t('system.operations.testDiscord')}
        </Button>
      </Group>
      <Text size="xs" c="dimmed" mt={4}>
        {botTokenSet && selectedTestChannel
          ? t('system.operations.testDiscordHint')
          : t('system.operations.testDiscordMissing')}
      </Text>
      {testResult?.kind === 'discord' && <TestChecklist state={testResult} />}
    </Card>
  );
}
