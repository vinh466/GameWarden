import { createFileRoute, Outlet, useLocation, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Tabs } from '@mantine/core';
import { PageHeader } from '@/components/PageHeader';

export const Route = createFileRoute('/_app/system')({
  component: SystemLayout,
});

function SystemLayout() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const path = useLocation({ select: (location) => location.pathname });
  const value =
    path === '/system/logs'
      ? 'logs'
      : path === '/system/notifications'
        ? 'notifications'
        : 'overview';

  return (
    <>
      <PageHeader title={t('system.title')} description={t('system.sectionHint')} />
      <Tabs
        value={value}
        onChange={(next) => {
          if (next === 'logs') {
            void navigate({ to: '/system/logs' });
          } else if (next === 'notifications') {
            void navigate({ to: '/system/notifications' });
          } else {
            void navigate({ to: '/system' });
          }
        }}
        mb="lg"
      >
        <Tabs.List>
          <Tabs.Tab value="overview">{t('system.tabs.overview')}</Tabs.Tab>
          <Tabs.Tab value="logs">{t('system.tabs.logs')}</Tabs.Tab>
          <Tabs.Tab value="notifications">{t('system.tabs.notifications')}</Tabs.Tab>
        </Tabs.List>
      </Tabs>
      <Outlet />
    </>
  );
}
