import { useState } from 'react';
import { IconArrowBackUp } from '@tabler/icons-react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Checkbox, Group, Modal, Stack, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { switchVersion } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { HelpButton } from '@/components/help/HelpButton';
import { useHelpHost } from '@/components/help/useHelp';

/**
 * docs/11 §2 System: *Switch back to <previous>*, the panel's `gamewarden version use`. Shown when one is installed.
 * The confirmation is a Modal of its own, so it can step aside for the help drawer (docs/12 §4).
 */
export function SwitchBack() {
  const { t } = useTranslation();
  const versions = useQuery(queries.versions());
  const [confirming, confirm] = useDisclosure(false);
  const helpHost = useHelpHost();
  const previous = versions.data?.previous;
  if (!versions.data || !previous) {
    return null;
  }

  const { current, canRestoreDatabase } = versions.data;
  return (
    <>
      <Button
        variant="subtle"
        size="xs"
        leftSection={<IconArrowBackUp size={14} />}
        onClick={confirm.open}
      >
        {t('system.switchBack', { version: previous })}
      </Button>
      <Modal
        opened={confirming}
        onClose={confirm.close}
        title={t('system.switchTitle', { version: previous })}
        {...helpHost}
      >
        <ConfirmSwitch
          version={previous}
          current={current}
          canRestoreDatabase={canRestoreDatabase}
          onDone={confirm.close}
        />
      </Modal>
    </>
  );
}

function ConfirmSwitch({
  version,
  current,
  canRestoreDatabase,
  onDone,
}: {
  version: string;
  current: string;
  canRestoreDatabase: boolean;
  onDone: () => void;
}) {
  const { t } = useTranslation();
  const [restoreDatabase, setRestoreDatabase] = useState(false);
  const run = useMutation({
    mutationFn: () => switchVersion({ body: { version, restoreDatabase }, throwOnError: true }),
    onSuccess: () => {
      notifications.show({
        color: 'violet',
        loading: true,
        message: t('system.switching', { version }),
        autoClose: 10_000,
      });
      onDone();
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  return (
    <Stack gap="md">
      <Text size="sm">{t('system.switchText', { version })}</Text>
      {canRestoreDatabase && (
        <Group gap={4} wrap="nowrap" align="flex-start">
          <Checkbox
            checked={restoreDatabase}
            onChange={(event) => setRestoreDatabase(event.currentTarget.checked)}
            label={t('system.restoreDb', { current })}
            description={t('system.restoreDbHint')}
          />
          <HelpButton
            label={t('system.restoreDbShort')}
            help={{ topicId: 'system.restoreDb', defaultValue: 'Off' }}
          />
        </Group>
      )}
      <Group justify="flex-end">
        <Button variant="default" onClick={onDone}>
          {t('actions.cancel')}
        </Button>
        <Button color="orange" loading={run.isPending} onClick={() => run.mutate()}>
          {t('system.switchBack', { version })}
        </Button>
      </Group>
    </Stack>
  );
}
