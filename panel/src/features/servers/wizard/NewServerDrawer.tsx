import { useTranslation } from 'react-i18next';
import { Drawer } from '@mantine/core';
import { useHelpHost } from '@/components/help/useHelp';
import { NewServerWizard } from './NewServerWizard';
import classes from './NewServerDrawer.module.css';

/**
 * The new server wizard in a wide drawer over the server list (Owner review, 2026-10-28). A click outside does not
 * close it, so a half-filled wizard is not lost by accident; Escape and the close button do. Closing discards it.
 * Built from Drawer's compound parts, so the wizard's footer sits beside Drawer.Body inside the scrolling content,
 * as Drawer.Header does at the top. Its fields have help buttons: it steps aside while the help drawer is open.
 */
export function NewServerDrawer({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const helpHost = useHelpHost();
  return (
    <Drawer.Root
      opened={opened}
      onClose={onClose}
      position="right"
      size="xl"
      closeOnClickOutside={false}
      {...helpHost}
      // The Styles API selector: Drawer.Content's className would also land on the positioning `inner` element.
      classNames={{ content: classes.content }}
    >
      <Drawer.Overlay />
      <Drawer.Content>
        <Drawer.Header>
          <Drawer.Title>{t('wizard.title')}</Drawer.Title>
          <Drawer.CloseButton aria-label={t('actions.close')} />
        </Drawer.Header>
        <NewServerWizard />
      </Drawer.Content>
    </Drawer.Root>
  );
}
