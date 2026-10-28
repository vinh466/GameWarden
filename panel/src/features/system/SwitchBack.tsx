import { useState } from 'react';
import { IconArrowBackUp } from '@tabler/icons-react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Checkbox, Group, Stack, Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { switchVersion } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';

/** docs/11 §2 System: *Switch back to <previous>*, the panel's `gamewarden version use`. Shown when one is installed. */
export function SwitchBack() {
  const { t } = useTranslation();
  const versions = useQuery(queries.versions());
  const previous = versions.data?.previous;
  if (!versions.data || !previous) {
    return null;
  }

  const { current, canRestoreDatabase } = versions.data;
  return (
    <Button
      variant="subtle"
      size="xs"
      leftSection={<IconArrowBackUp size={14} />}
      onClick={() => {
        const id = modals.open({
          title: t('system.switchTitle', { version: previous }),
          children: (
            <ConfirmSwitch
              version={previous}
              current={current}
              canRestoreDatabase={canRestoreDatabase}
              onDone={() => modals.close(id)}
            />
          ),
        });
      }}
    >
      {t('system.switchBack', { version: previous })}
    </Button>
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
        <Checkbox
          checked={restoreDatabase}
          onChange={(event) => setRestoreDatabase(event.currentTarget.checked)}
          label={t('system.restoreDb', { current })}
          description={t('system.restoreDbHint')}
        />
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
