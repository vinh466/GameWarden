import { IconPlugConnectedX } from '@tabler/icons-react';
import { Outlet } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Alert, AppShell, Container } from '@mantine/core';
import { useDebouncedValue, useDisclosure, useLocalStorage } from '@mantine/hooks';
import { useRealtime } from '@/api/realtime';
import { useRealtimeStatus } from '@/api/realtimeStatus';
import { AppSpotlight } from './AppSpotlight';
import { HeaderBar } from './HeaderBar';
import { Navbar } from './Navbar';

/** The shell of every signed-in page (docs/11 §3): Mantine AppShell with a navbar that collapses on desktop and phone. */
export function AppLayout() {
  useRealtime();
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
          <LinkLostBanner />
          <Outlet />
        </Container>
      </AppShell.Main>
      <AppSpotlight />
    </AppShell>
  );
}

/** docs/11 §2: a red banner while the realtime link is down; a short blip during a Core restart does not show. */
function LinkLostBanner() {
  const { t } = useTranslation();
  const status = useRealtimeStatus();
  const [lost] = useDebouncedValue(status === 'reconnecting' || status === 'disconnected', 3_000);
  if (!lost || status === 'connected') {
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
