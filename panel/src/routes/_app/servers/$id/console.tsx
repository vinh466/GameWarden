import { useState } from 'react';
import { IconDownload, IconSearch, IconSend } from '@tabler/icons-react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Group, Stack, Text, TextInput, Tooltip } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { sendConsoleCommand } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { useConsoleSubscription } from '@/api/realtime';
import { ConsoleView } from '@/features/console/ConsoleView';

export const Route = createFileRoute('/_app/servers/$id/console')({
  loader: ({ context: { queryClient }, params }) =>
    queryClient.query({ ...queries.console(params.id), staleTime: 'static' }),
  component: Console,
});

function Console() {
  const { t } = useTranslation();
  const { id } = Route.useParams();
  const server = useQuery(queries.instance(id));
  const console = useQuery(queries.console(id));
  useConsoleSubscription(id); // new lines arrive over the realtime link while this tab is open
  const [search, setSearch] = useState('');
  const [debouncedSearch] = useDebouncedValue(search, 150);
  const lines = console.data?.lines ?? [];
  const matches = debouncedSearch
    ? lines.filter((line) => line.text.toLowerCase().includes(debouncedSearch.toLowerCase())).length
    : null;
  const canSend = server.data?.state === 'running' || server.data?.state === 'starting';

  return (
    <Stack gap="sm">
      <Group justify="space-between" gap="xs">
        <TextInput
          placeholder={t('console.search')}
          leftSection={<IconSearch size={16} />}
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          rightSection={
            matches !== null ? (
              <Text size="xs" c="dimmed" pr="xs">
                {matches}
              </Text>
            ) : null
          }
          rightSectionWidth={matches !== null ? 48 : undefined}
          flex={1}
          maw={420}
          aria-label={t('console.search')}
        />
        <Tooltip label={t('console.download')}>
          <ActionIcon
            variant="default"
            size="lg"
            aria-label={t('console.download')}
            onClick={() =>
              download(
                id,
                lines.map((line) => `${line.at} ${line.text}`)
              )
            }
          >
            <IconDownload size={16} />
          </ActionIcon>
        </Tooltip>
      </Group>
      <ConsoleView lines={lines} search={debouncedSearch} height="calc(100dvh - 380px)" />
      <CommandBox id={id} disabledReason={canSend ? null : t('console.notRunning')} />
    </Stack>
  );
}

function CommandBox({ id, disabledReason }: { id: string; disabledReason: string | null }) {
  const { t } = useTranslation();
  const [command, setCommand] = useState('');
  const [history, setHistory] = useState<string[]>([]);
  const [cursor, setCursor] = useState<number | null>(null);
  const send = useMutation({
    mutationFn: (text: string) =>
      sendConsoleCommand({ path: { id }, body: { command: text }, throwOnError: true }),
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const submit = () => {
    const text = command.trim();
    if (!text || disabledReason) {
      return;
    }

    send.mutate(text);
    setHistory((previous) => [...previous.filter((entry) => entry !== text), text].slice(-50));
    setCursor(null);
    setCommand('');
  };

  return (
    <TextInput
      ff="monospace"
      placeholder={disabledReason ?? t('console.command')}
      disabled={!!disabledReason}
      value={command}
      onChange={(event) => setCommand(event.currentTarget.value)}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          submit();
        } else if (event.key === 'ArrowUp' && history.length > 0) {
          event.preventDefault();
          const next = cursor === null ? history.length - 1 : Math.max(0, cursor - 1);
          setCursor(next);
          setCommand(history[next] ?? '');
        } else if (event.key === 'ArrowDown' && cursor !== null) {
          event.preventDefault();
          const next = cursor + 1;
          setCursor(next >= history.length ? null : next);
          setCommand(next >= history.length ? '' : (history[next] ?? ''));
        }
      }}
      rightSection={
        <ActionIcon
          variant="filled"
          color="violet"
          onClick={submit}
          loading={send.isPending}
          disabled={!!disabledReason || !command.trim()}
          aria-label={t('console.send')}
        >
          <IconSend size={14} />
        </ActionIcon>
      }
      aria-label={t('console.command')}
    />
  );
}

function download(id: string, lines: string[]) {
  const url = URL.createObjectURL(new Blob([lines.join('\n')], { type: 'text/plain' }));
  const link = Object.assign(document.createElement('a'), {
    href: url,
    download: `${id}-console.log`,
  });
  link.click();
  URL.revokeObjectURL(url);
}
