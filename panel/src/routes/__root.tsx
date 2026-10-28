import { useEffect } from 'react';
import type { QueryClient } from '@tanstack/react-query';
import { createRootRouteWithContext, Outlet, useRouterState } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Center, Stack, Text, Title } from '@mantine/core';
import { NavigationProgress, nprogress } from '@mantine/nprogress';
import { ButtonLink } from '@/components/links';

export const Route = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  component: Root,
  notFoundComponent: NotFound,
});

function Root() {
  // The thin top bar while a route loads its data.
  const loading = useRouterState({ select: (state) => state.status === 'pending' });
  useEffect(() => {
    if (loading) {
      nprogress.start();
    } else {
      nprogress.complete();
    }
  }, [loading]);

  return (
    <>
      {/* Decorative: the page itself tells assistive tech when it has loaded. */}
      <NavigationProgress size={2} aria-hidden />
      <Outlet />
    </>
  );
}

function NotFound() {
  const { t } = useTranslation();
  return (
    <Center mih="60vh">
      <Stack align="center" gap="sm">
        <Title order={1} c="dimmed">
          404
        </Title>
        <Text>{t('errors.notFound')}</Text>
        <ButtonLink to="/" variant="light">
          {t('errors.goHome')}
        </ButtonLink>
      </Stack>
    </Center>
  );
}
