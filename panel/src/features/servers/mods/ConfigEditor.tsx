import dayjs from 'dayjs';
import { useState } from 'react';
import { IconAlertTriangle, IconPencil } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Button,
  Code,
  Group,
  JsonInput,
  Skeleton,
  Stack,
  Text,
  Textarea,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import {
  updateInstanceModsConfigContent,
  type InstanceSnapshot,
  type ModConfigSaved,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { ProblemAlert } from '@/components/ProblemAlert';
import { DiffView } from './DiffView';
import { fileSize } from './fileSize';
import { refreshConfig, useModsFailure } from './useModsSync';
import { Why } from './Why';
import classes from './ConfigEditor.module.css';

/** The Owner's text and the file it started from: its text and SHA-256 (the save's `baseSha256`). */
interface Draft {
  text: string;
  original: string;
  base: string;
}

/**
 * A config file, read-only until *Edit* (ADR-0021): a monospace text editor (JSON checked as it is typed), *Save* writes
 * the file the server uses at once. When the file changed on the server since it was loaded (409 `config_changed`),
 * the Owner's text stays, and the server's version can be loaded or compared with it.
 */
export function ConfigEditor({
  server,
  path,
  onSaved,
}: {
  server: InstanceSnapshot;
  path: string;
  onSaved: (result: ModConfigSaved) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const failed = useModsFailure();
  const content = useQuery(queries.modConfig(server.id, path));
  const [draft, setDraft] = useState<Draft | null>(null);
  const [conflict, setConflict] = useState(false);
  const [comparing, setComparing] = useState(false);
  const save = useMutation({
    mutationFn: async ({ text, base }: Draft) =>
      (
        await updateInstanceModsConfigContent({
          path: { id: server.id },
          query: { path },
          body: { content: text, baseSha256: base },
          throwOnError: true,
        })
      ).data,
    onSuccess: (result) => {
      setDraft(null);
      setConflict(false);
      notifications.show({ color: 'violet', message: t('mods.configs.saved', { path }) });
      onSaved(result);
    },
    onError: (error) => {
      if (toProblem(error, '').code === 'config_changed') {
        setConflict(true);
        setComparing(false);
      } else {
        failed(server.displayName, error);
      }
    },
    onSettled: () => refreshConfig(queryClient, server.id, path),
  });

  if (content.isError) {
    return <ProblemAlert error={content.error} />;
  }

  if (!content.data) {
    return <Skeleton h={240} />;
  }

  const file = content.data;
  if (!draft) {
    return (
      <Stack gap="sm">
        <Group justify="space-between" gap="xs">
          <Text size="xs" c="dimmed">
            {fileSize(file.size)} ·{' '}
            {t('mods.configs.modifiedAt', { time: dayjs(file.modified).fromNow() })}
          </Text>
          <Why reason={file.editable ? null : t('mods.configs.notEditable')}>
            <Button
              size="xs"
              leftSection={<IconPencil size={16} aria-hidden />}
              disabled={!file.editable}
              onClick={() => setDraft({ text: file.text, original: file.text, base: file.sha256 })}
            >
              {t('mods.configs.edit')}
            </Button>
          </Why>
        </Group>
        <Code block fz="xs">
          {file.text}
        </Code>
      </Stack>
    );
  }

  const reload = () => {
    setDraft({ text: file.text, original: file.text, base: file.sha256 });
    setConflict(false);
    setComparing(false);
  };
  const editor = {
    value: draft.text,
    autosize: true,
    minRows: 12,
    maxRows: 30,
    'aria-label': t('mods.configs.editorLabel', { path }),
    classNames: { input: classes.editor },
    disabled: save.isPending,
  };
  return (
    <Stack gap="sm">
      {conflict && (
        <Alert
          color="orange"
          icon={<IconAlertTriangle aria-hidden />}
          title={t('mods.configs.conflictTitle')}
        >
          <Stack gap="xs">
            <Text size="sm">{t('mods.configs.conflictText', { path })}</Text>
            <Group gap="xs">
              <Button
                size="xs"
                variant="light"
                color="orange"
                loading={content.isFetching}
                onClick={reload}
              >
                {t('mods.configs.reload')}
              </Button>
              <Button size="xs" variant="default" onClick={() => setComparing((value) => !value)}>
                {t(comparing ? 'mods.configs.hideCompare' : 'mods.configs.compare')}
              </Button>
            </Group>
            {comparing && (
              <DiffView
                oldText={file.text}
                newText={draft.text}
                legend={t('mods.configs.compareServerLegend')}
              />
            )}
          </Stack>
        </Alert>
      )}
      <Text size="xs" c="dimmed">
        {t('mods.configs.editHint')}
      </Text>
      {path.toLowerCase().endsWith('.json') ? (
        <JsonInput
          {...editor}
          validationError={t('mods.configs.invalidJson')}
          onChange={(text) => setDraft({ ...draft, text })}
        />
      ) : (
        <Textarea
          {...editor}
          onChange={(event) => setDraft({ ...draft, text: event.currentTarget.value })}
        />
      )}
      <Group justify="flex-end" gap="xs">
        <Button
          variant="default"
          disabled={save.isPending}
          onClick={() => {
            setDraft(null);
            setConflict(false);
          }}
        >
          {t('actions.cancel')}
        </Button>
        <Why
          reason={draft.text === draft.original && !conflict ? t('mods.configs.unchanged') : null}
        >
          <Button
            loading={save.isPending}
            disabled={draft.text === draft.original && !conflict}
            onClick={() => save.mutate(draft)}
          >
            {t('actions.save')}
          </Button>
        </Why>
      </Group>
    </Stack>
  );
}
