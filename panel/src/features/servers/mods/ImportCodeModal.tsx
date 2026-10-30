import dayjs from 'dayjs';
import { IconAlertTriangle } from '@tabler/icons-react';
import { useMutation } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import {
  Alert,
  Badge,
  Button,
  Group,
  List,
  Modal,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core';
import { schemaResolver, useForm } from '@mantine/form';
import { previewInstanceModsCode, type ModCodePreview } from '@/api/generated';
import { HelpField } from '@/components/help/HelpLabel';
import { useHelpHost } from '@/components/help/useHelp';
import { ProblemAlert } from '@/components/ProblemAlert';
import { displayName } from './modEntries';
import { useModsSync } from './useModsSync';

/** A UUID, as r2modman's codes are (Core's `R2ModmanCode.TryNormalizeCode`); case and spaces do not matter. */
const codePattern = /^\s*[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\s*$/i;
const stateColors = { ok: 'gray', deprecated: 'yellow', notFound: 'red' } as const;

/**
 * Import an r2modman code (docs/13 §4.4 item 1): paste → preview (nothing changes) → the mods with their versions,
 * *enabled* and Thunderstore state, the warnings and config files → *Apply* builds a set from exactly that preview.
 */
export function ImportCodeModal({
  id,
  server,
  opened,
  onClose,
}: {
  id: string;
  server: string;
  opened: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const helpHost = useHelpHost();
  const form = useForm({
    mode: 'uncontrolled',
    initialValues: { code: '' },
    validate: schemaResolver(
      z.object({ code: z.string().regex(codePattern, t('mods.import.codeInvalid')) }),
      { sync: true }
    ),
  });
  const preview = useMutation({
    mutationFn: async (code: string) =>
      (await previewInstanceModsCode({ path: { id }, body: { code }, throwOnError: true })).data,
  });
  const apply = useModsSync(id, server);
  const close = () => {
    preview.reset();
    form.reset();
    onClose();
  };

  return (
    <Modal opened={opened} onClose={close} size="xl" {...helpHost} title={t('mods.import.title')}>
      {preview.data ? (
        <Preview
          preview={preview.data}
          applying={apply.isPending}
          onBack={() => preview.reset()}
          onApply={() =>
            apply.mutate({ previewId: preview.data.previewId }, { onSuccess: () => close() })
          }
        />
      ) : (
        <form onSubmit={form.onSubmit((values) => preview.mutate(values.code.trim()))}>
          <Stack gap="md">
            {preview.isError && <ProblemAlert error={preview.error} />}
            <HelpField label={t('mods.import.code')} help={{ topicId: 'mods.codes' }}>
              {(inputId) => (
                <TextInput
                  id={inputId}
                  description={t('mods.import.codeHint')}
                  placeholder="0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13"
                  ff="monospace"
                  data-autofocus
                  key={form.key('code')}
                  {...form.getInputProps('code')}
                />
              )}
            </HelpField>
            <Group justify="space-between">
              <Text size="xs" c="dimmed">
                {t('mods.import.previewHint')}
              </Text>
              <Button type="submit" loading={preview.isPending}>
                {t('mods.import.preview')}
              </Button>
            </Group>
          </Stack>
        </form>
      )}
    </Modal>
  );
}

function Preview({
  preview,
  applying,
  onBack,
  onApply,
}: {
  preview: ModCodePreview;
  applying: boolean;
  onBack: () => void;
  onApply: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap="md">
      <Group gap="xs">
        <Text fw={600}>{t('mods.import.profile', { name: preview.profileName })}</Text>
        <Text size="sm" c="dimmed" ff="monospace">
          {preview.code}
        </Text>
      </Group>
      <Table.ScrollContainer minWidth={420}>
        <Table verticalSpacing={4}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('mods.columns.mod')}</Table.Th>
              <Table.Th>{t('mods.columns.version')}</Table.Th>
              <Table.Th>{t('mods.columns.state')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {preview.entries.map((entry) => (
              <Table.Tr key={entry.package}>
                <Table.Td>
                  <Text size="sm" fw={500}>
                    {displayName(entry.package)}
                  </Text>
                  <Text size="xs" c="dimmed" ff="monospace">
                    {entry.package}
                    {entry.enabled ? '' : ` · ${t('mods.import.disabledInCode')}`}
                  </Text>
                </Table.Td>
                <Table.Td ff="monospace">{entry.version}</Table.Td>
                <Table.Td>
                  <Badge variant="light" color={stateColors[entry.state]}>
                    {t(`mods.state.${entry.state}`)}
                  </Badge>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      {preview.warnings.length > 0 && (
        <Alert
          color="yellow"
          icon={<IconAlertTriangle aria-hidden />}
          title={t('mods.import.warnings')}
        >
          <List size="sm" spacing={4}>
            {preview.warnings.map((warning) => (
              <List.Item key={warning}>{warning}</List.Item>
            ))}
          </List>
        </Alert>
      )}
      {preview.configFiles.length > 0 && (
        <Text size="sm">{t('mods.import.configFiles', { count: preview.configFiles.length })}</Text>
      )}
      <Text size="xs" c="dimmed">
        {t('mods.import.applyHint')}{' '}
        {t('mods.import.expires', { time: dayjs(preview.expiresAt).format('LT') })}
      </Text>
      <Group justify="space-between">
        <Button variant="default" onClick={onBack}>
          {t('mods.import.otherCode')}
        </Button>
        <Button loading={applying} onClick={onApply}>
          {t('mods.import.apply')}
        </Button>
      </Group>
    </Stack>
  );
}
