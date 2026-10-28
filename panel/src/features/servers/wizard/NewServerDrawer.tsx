import { useTranslation } from 'react-i18next';
import { Drawer } from '@mantine/core';
import { NewServerWizard } from './NewServerWizard';

/**
 * The new server wizard in a wide drawer over the server list (Owner review, 2026-10-28). A click outside does not
 * close it, so a half-filled wizard is not lost by accident; Escape and the close button do. Closing discards it.
 */
export function NewServerDrawer({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <Drawer
      opened={opened}
      onClose={onClose}
      position="right"
      size="xl"
      title={t('wizard.title')}
      closeOnClickOutside={false}
    >
      <NewServerWizard />
    </Drawer>
  );
}
