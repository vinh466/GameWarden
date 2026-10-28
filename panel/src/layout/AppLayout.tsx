import { Outlet } from '@tanstack/react-router';
import { AppShell, Container } from '@mantine/core';
import { useDisclosure, useLocalStorage } from '@mantine/hooks';
import { AppSpotlight } from './AppSpotlight';
import { HeaderBar } from './HeaderBar';
import { Navbar } from './Navbar';

/** The shell of every signed-in page (docs/11 §3): Mantine AppShell with a navbar that collapses on desktop and phone. */
export function AppLayout() {
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
          <Outlet />
        </Container>
      </AppShell.Main>
      <AppSpotlight />
    </AppShell>
  );
}
