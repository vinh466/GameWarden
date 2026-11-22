import { useState } from 'react';
import { IconFileText, IconHistory, IconInfoCircle } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Drawer, Stack, Tabs, Text } from '@mantine/core';
import { modals } from '@mantine/modals';
import type { InstanceSnapshot, ModConfigSaved } from '@/api/generated';
import { useHelpHost } from '@/components/help/useHelp';
import { UnsavedChangesGuard } from '@/components/UnsavedChangesGuard';
import { useServerCommands } from '../useServerCommands';
import { ConfigEditor } from './ConfigEditor';
import { ConfigHistory } from './ConfigHistory';

/** One config file (docs/13 §4.7, ADR-0021): the file, viewed or edited, and its history. */
export function ConfigDrawer({
  server,
  path,
  onClose,
}: {
  server: InstanceSnapshot;
  path: string | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const helpHost = useHelpHost();
  const [dirty, setDirty] = useState(false);

  // Closing the drawer loses a draft exactly like leaving the page, so it asks first (docs/22 §11.1, B3).
  const close = () => {
    if (!dirty) {
      onClose();
      return;
    }
    modals.openConfirmModal({
      title: t('unsaved.title'),
      children: <Text size="sm">{t('unsaved.text')}</Text>,
      labels: { confirm: t('unsaved.leave'), cancel: t('unsaved.stay') },
      confirmProps: { color: 'red' },
      zIndex: 300,
      onConfirm: () => {
        setDirty(false);
        onClose();
      },
    });
  };

  return (
    <>
      <UnsavedChangesGuard active={dirty} />
      <Drawer
        opened={!!path}
        onClose={close}
        position="right"
        size="xl"
        title={
          <Text ff="monospace" fw={600} size="sm">
            {path}
          </Text>
        }
        {...helpHost}
      >
        {path && <ConfigFile key={path} server={server} path={path} onDirtyChange={setDirty} />}
      </Drawer>
    </>
  );
}

function ConfigFile({
  server,
  path,
  onDirtyChange,
}: {
  server: InstanceSnapshot;
  path: string;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const { t } = useTranslation();
  const { run, pending } = useServerCommands();
  const [tab, setTab] = useState<string | null>('file');
  const [saved, setSaved] = useState<ModConfigSaved | null>(null);
  return (
    <Stack gap="sm">
      {saved?.running && (
        <Alert
          color="blue"
          icon={<IconInfoCircle aria-hidden />}
          title={t('mods.configs.savedRunningTitle')}
          withCloseButton
          closeButtonLabel={t('mods.configs.dismiss')}
          onClose={() => setSaved(null)}
        >
          <Stack gap="xs" align="flex-start">
            <Text size="sm">{t('mods.configs.savedRunning')}</Text>
            <Button
              size="xs"
              variant="light"
              loading={pending(server) === 'restart'}
              onClick={() => run(server, 'restart')}
            >
              {t('settings.restartNow')}
            </Button>
          </Stack>
        </Alert>
      )}
      <Tabs value={tab} onChange={setTab}>
        <Tabs.List>
          <Tabs.Tab value="file" leftSection={<IconFileText size={16} aria-hidden />}>
            {t('mods.configs.tabFile')}
          </Tabs.Tab>
          <Tabs.Tab value="history" leftSection={<IconHistory size={16} aria-hidden />}>
            {t('mods.configs.tabHistory')}
          </Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="file" pt="sm" keepMounted>
          <ConfigEditor
            server={server}
            path={path}
            onSaved={setSaved}
            onDirtyChange={onDirtyChange}
          />
        </Tabs.Panel>
        <Tabs.Panel value="history" pt="sm">
          <ConfigHistory
            server={server}
            path={path}
            onRestored={(result) => {
              setSaved(result);
              setTab('file');
            }}
          />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  );
}
