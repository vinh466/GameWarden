import { IconKey, IconPlus, IconRobot, IconTrash } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Button,
  Card,
  Divider,
  Group,
  NumberInput,
  PasswordInput,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import type { AgentModelSettings } from '@/api/generated';
import { TestChecklist } from './TestChecklist';
import type { TestState } from './types';

/** The OpenAI-compatible model the Agent calls: endpoint, limits, extra headers and the write-only key. */
export function ModelSettingsCard({
  modelKeySet,
  model,
  modelKey,
  onModelKeyChange,
  testResult,
  onModelChange,
  onHeadersChange,
  onSaveSecret,
  savingSecret,
  onTest,
  testing,
}: {
  modelKeySet: boolean;
  model: AgentModelSettings;
  modelKey: string;
  onModelKeyChange: (value: string) => void;
  testResult: TestState | null;
  onModelChange: (patch: Partial<AgentModelSettings>) => void;
  onHeadersChange: (next: Record<string, string>) => void;
  onSaveSecret: (name: string, value: string) => void;
  savingSecret: string | null;
  onTest: () => void;
  testing: boolean;
}) {
  const { t } = useTranslation();
  const headers = model.headers ?? {};

  return (
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
          onChange={(event) => onModelChange({ baseUrl: event.currentTarget.value })}
          placeholder="https://model.example/v1"
        />
        <TextInput
          label={t('system.operations.modelId')}
          value={model.model ?? ''}
          onChange={(event) => onModelChange({ model: event.currentTarget.value })}
          placeholder="deepseek-v4.1-flash"
        />
        <NumberInput
          label={t('system.operations.maxTokens')}
          value={model.maxTokens ?? 4096}
          onChange={(value) => onModelChange({ maxTokens: Number(value) || 4096 })}
          min={256}
          max={32768}
        />
        <NumberInput
          label={t('system.operations.maxToolCalls')}
          value={model.maxToolCalls ?? 12}
          onChange={(value) => onModelChange({ maxToolCalls: Number(value) || 12 })}
          min={1}
          max={32}
        />
        <NumberInput
          label={t('system.operations.timeout')}
          value={model.timeoutSeconds ?? 120}
          onChange={(value) => onModelChange({ timeoutSeconds: Number(value) || 120 })}
          min={10}
          max={600}
        />
        <NumberInput
          label={t('system.operations.temperature')}
          value={model.temperature ?? 0.2}
          onChange={(value) => onModelChange({ temperature: Number(value) || 0.2 })}
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
            onHeadersChange({ ...headers, [`X-Custom-${Object.keys(headers).length + 1}`]: '' })
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
                onHeadersChange(next);
              }}
              flex={1}
            />
            <TextInput
              label={t('system.operations.headerValue')}
              value={value}
              onChange={(event) =>
                onHeadersChange({ ...headers, [name]: event.currentTarget.value })
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
                onHeadersChange(next);
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
            modelKeySet ? t('system.operations.secretSet') : t('system.operations.secretNotSet')
          }
          value={modelKey}
          onChange={(event) => onModelKeyChange(event.currentTarget.value)}
          leftSection={<IconKey size={16} />}
          flex={1}
        />
        <Button
          variant="light"
          loading={savingSecret === 'model-api-key'}
          disabled={!modelKey.trim()}
          onClick={() => onSaveSecret('model-api-key', modelKey)}
        >
          {t('system.operations.saveKey')}
        </Button>
      </Group>
      <Group mt="md" justify="space-between" align="center">
        <Text size="xs" c="dimmed">
          {modelKeySet
            ? t('system.operations.testModelHint')
            : t('system.operations.testModelMissing')}
        </Text>
        <Button
          variant="default"
          loading={testing}
          disabled={!modelKeySet || !model.baseUrl || !model.model}
          onClick={onTest}
        >
          {t('system.operations.testModel')}
        </Button>
      </Group>
      {testResult?.kind === 'model' && <TestChecklist state={testResult} />}
    </Card>
  );
}
