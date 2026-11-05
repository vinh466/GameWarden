import { useEffect } from 'react';
import { IconPlugConnectedX } from '@tabler/icons-react';
import { Outlet, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Alert, AppShell, Container } from '@mantine/core';
import { useDebouncedValue, useDisclosure, useLocalStorage } from '@mantine/hooks';
import { useRealtime } from '@/api/realtime';
import { useRealtimeStatus } from '@/api/realtimeStatus';
import { ReleaseNotesModal } from '@/features/system/update/ReleaseNotesModal';
import { UpdateBanner } from '@/features/system/update/UpdateBanner';
import { UpdateDialog } from '@/features/system/update/UpdateDialog';
import { isUpdateBannerVisible, useUpdateState } from '@/features/system/update/updateStore';
import { useUpdateWatcher } from '@/features/system/update/useUpdateWatcher';
import { AppSpotlight } from './AppSpotlight';
import { HeaderBar } from './HeaderBar';
import { Navbar } from './Navbar';
import { useTabStatus } from './useTabStatus';

/** The shell of every signed-in page (docs/11 §3): Mantine AppShell with a navbar that collapses on desktop and phone. */
export function AppLayout() {
  useRealtime();
  useTabStatus();
  useGoShortcuts();
  useUpdateWatcher();
  const [mobileOpened, { toggle: toggleMobile, close: closeMobile }] = useDisclosure();
  const [desktopOpened, setDesktopOpened] = useLocalStorage({
    key: 'gamewarden.navbar',
    defaultValue: true,
  });

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{
        width: 248,
        breakpoint: 'sm',
        collapsed: { mobile: !mobileOpened, desktop: !desktopOpened },
      }}
      padding={{ base: 'sm', sm: 'lg' }}
    >
      <AppShell.Header>
        <HeaderBar
          mobileOpened={mobileOpened}
          desktopOpened={desktopOpened}
          onToggleMobile={toggleMobile}
          onToggleDesktop={() => setDesktopOpened((opened) => !opened)}
        />
      </AppShell.Header>
      <AppShell.Navbar p="xs">
        <Navbar onNavigate={closeMobile} />
      </AppShell.Navbar>
      <AppShell.Main>
        <Container size={1400} px={0}>
          <UpdateBanner />
          <LinkLostBanner />
          <Outlet />
        </Container>
      </AppShell.Main>
      <AppSpotlight />
      <UpdateDialog />
      <ReleaseNotesModal />
    </AppShell>
  );
}

/** docs/11 §6.6: g then d opens the dashboard, g then s opens System; typing in a field never triggers it. */
function useGoShortcuts() {
  const navigate = useNavigate();
  useEffect(() => {
    let lastG = 0;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.metaKey || event.ctrlKey || event.altKey) {
        return;
      }

      const target = event.target as HTMLElement | null;
      if (
        target?.isContentEditable ||
        ['INPUT', 'TEXTAREA', 'SELECT'].includes(target?.tagName ?? '')
      ) {
        return;
      }

      const now = Date.now();
      const key = event.key.toLowerCase();
      if (key === 'g') {
        lastG = now;
        return;
      }

      if (now - lastG > 1_000) {
        return;
      }

      if (key === 'd' || key === 's') {
        event.preventDefault();
        void navigate({ to: key === 'd' ? '/' : '/system' });
      }
      lastG = 0;
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [navigate]);
}

/** docs/11 §2: a red banner while the realtime link is down; a short blip during a Core restart does not show. */
function LinkLostBanner() {
  const { t } = useTranslation();
  const status = useRealtimeStatus();
  const update = useUpdateState();
  const [lost] = useDebouncedValue(status === 'reconnecting' || status === 'disconnected', 3_000);
  if (isUpdateBannerVisible(update) || !lost || status === 'connected') {
    return null;
  }

  return (
    <Alert
      color="red"
      icon={<IconPlugConnectedX aria-hidden />}
      mb="md"
      title={t('core.disconnected')}
    >
      {t('core.disconnectedText')}
    </Alert>
  );
}
