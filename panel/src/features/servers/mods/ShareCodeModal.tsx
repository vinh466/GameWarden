import dayjs from 'dayjs';
import { IconCheck, IconCopy } from '@tabler/icons-react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Code, CopyButton, Group, List, Modal, Stack, Text } from '@mantine/core';
import { createInstanceModsShareCode, type ModsView } from '@/api/generated';
import { HelpButton } from '@/components/help/HelpButton';
import { useHelpHost } from '@/components/help/useHelp';
import { ProblemAlert } from '@/components/ProblemAlert';
import { refreshMods } from './useModsSync';

/**
 * The share code for friends (docs/13 §4.6): an r2modman code of the active set's enabled mods, made on demand, with
 * *Copy*, its time and how to import it. It carries no config files; Thunderstore may let old codes expire.
 */
export function ShareCodeModal({
  id,
  mods,
  opened,
  onClose,
}: {
  id: string;
  mods: ModsView;
  opened: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const helpHost = useHelpHost();
  const create = useMutation({
    mutationFn: async () =>
      (await createInstanceModsShareCode({ path: { id }, throwOnError: true })).data,
    onSettled: () => refreshMods(queryClient, id),
  });
  const code = create.data?.code ?? mods.shareCode;
  const createdAt = create.data?.createdAt ?? mods.shareCodeCreatedAt;
  const close = () => {
    create.reset();
    onClose();
  };

  return (
    <Modal opened={opened} onClose={close} size="lg" title={t('mods.share.title')} {...helpHost}>
      <Stack gap="md">
        <Group gap={4} align="flex-start" wrap="nowrap">
          <Text size="sm">{t('mods.share.text')}</Text>
          <HelpButton label={t('mods.share.title')} help={{ topicId: 'mods.codes' }} />
        </Group>
        {create.isError && <ProblemAlert error={create.error} />}
        {code ? (
          <Stack gap={6}>
            <Text size="sm" fw={500}>
              {t('mods.share.code')}
            </Text>
            <Group gap="xs" wrap="nowrap">
              <Code fz="md" py={6} px="sm" flex={1}>
                {code}
              </Code>
              <CopyButton value={code}>
                {({ copied, copy }) => (
                  <Button
                    variant={copied ? 'light' : 'filled'}
                    color={copied ? 'green' : undefined}
                    leftSection={copied ? <IconCheck size={16} /> : <IconCopy size={16} />}
                    onClick={copy}
                  >
                    {copied ? t('actions.copied') : t('actions.copy')}
                  </Button>
                )}
              </CopyButton>
            </Group>
            {createdAt && (
              <Text size="xs" c="dimmed" title={dayjs(createdAt).format('LLL')}>
                {t('mods.share.created', { time: dayjs(createdAt).fromNow() })}
              </Text>
            )}
          </Stack>
        ) : (
          <Text size="sm" c="dimmed">
            {t('mods.share.none')}
          </Text>
        )}
        <List size="sm" spacing={4}>
          <List.Item>{t('mods.share.steps')}</List.Item>
          <List.Item>{t('mods.share.expiry')}</List.Item>
        </List>
        <Alert color="blue" variant="light">
          <Text size="sm">{t('mods.share.noConfigs')}</Text>
        </Alert>
        <Group justify="flex-end">
          <Button
            variant={code ? 'default' : 'filled'}
            loading={create.isPending}
            onClick={() => create.mutate()}
          >
            {code ? t('mods.share.createAgain') : t('mods.share.create')}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
