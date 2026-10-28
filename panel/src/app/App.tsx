import { useMemo } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Drawer, MantineProvider, mergeThemeOverrides, Modal, Notification } from '@mantine/core';
import { DatesProvider } from '@mantine/dates';
import { ModalsProvider } from '@mantine/modals';
import { Notifications } from '@mantine/notifications';
import { cssVariablesResolver, theme } from '../theme';
import { queryClient, router } from './router';

export function App() {
  const { t, i18n } = useTranslation();
  // Every close button says what it does, in the panel's language (axe: button-name).
  const themed = useMemo(() => {
    const close = { closeButtonProps: { 'aria-label': t('actions.close') } };
    return mergeThemeOverrides(theme, {
      components: {
        Drawer: Drawer.extend({ defaultProps: close }),
        Modal: Modal.extend({ defaultProps: close }),
        Notification: Notification.extend({ defaultProps: close }),
      },
    });
  }, [t]);

  return (
    <MantineProvider
      theme={themed}
      cssVariablesResolver={cssVariablesResolver}
      defaultColorScheme="auto"
    >
      <DatesProvider settings={{ locale: i18n.language.startsWith('vi') ? 'vi' : 'en' }}>
        <QueryClientProvider client={queryClient}>
          <ModalsProvider>
            {/* Bottom right: the header's actions stay reachable while toasts show (found by the e2e smoke test). */}
            <Notifications position="bottom-right" limit={4} />
            <RouterProvider router={router} />
          </ModalsProvider>
        </QueryClientProvider>
      </DatesProvider>
    </MantineProvider>
  );
}
