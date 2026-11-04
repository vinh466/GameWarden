import { useEffect, useRef } from 'react';
import {
  IconActivity,
  IconBell,
  IconFileText,
  IconLayoutDashboard,
  IconListCheck,
  IconPuzzle,
  IconRobot,
} from '@tabler/icons-react';
import { createFileRoute, Outlet, useLocation, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ScrollArea, Tabs, Text } from '@mantine/core';
import { PageHeader } from '@/components/PageHeader';
import {
  systemTabOfPath,
  systemTabPaths,
  systemTabs,
  type SystemTab,
} from '@/features/system/systemTabs';

const icons = {
  overview: IconLayoutDashboard,
  jobs: IconListCheck,
  activity: IconActivity,
  adapters: IconPuzzle,
  agents: IconRobot,
  logs: IconFileText,
  notifications: IconBell,
} satisfies Record<SystemTab, typeof IconActivity>;

export const Route = createFileRoute('/_app/system')({
  component: SystemLayout,
});

/** The System hub: one page header, then the peer views as tabs, each on its own route (docs/11 §4). */
function SystemLayout() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const path = useLocation({ select: (location) => location.pathname });
  const current = systemTabOfPath(path);
  const listRef = useRef<HTMLDivElement>(null);

  // Keep the active tab visible when a phone opens a deep link such as /system/logs.
  useEffect(() => {
    listRef.current
      ?.querySelector<HTMLElement>('[role="tab"][data-active="true"]')
      ?.scrollIntoView({ inline: 'center', block: 'nearest' });
  }, [current]);

  return (
    <>
      <PageHeader title={t('system.title')} description={t('system.sectionHint')} />
      <Tabs
        value={current}
        onChange={(value) => {
          if (value) {
            void navigate({ to: systemTabPaths[value as SystemTab] });
          }
        }}
        mb="lg"
      >
        <ScrollArea type="auto" scrollbarSize={6}>
          <Tabs.List
            ref={listRef}
            style={{ flexWrap: 'nowrap' }}
            aria-label={t('system.tabsLabel')}
          >
            {systemTabs.map((tab) => {
              const Icon = icons[tab];
              return (
                <Tabs.Tab key={tab} value={tab} leftSection={<Icon size={16} />}>
                  <Text size="sm" inherit>
                    {t(`system.tabs.${tab}`)}
                  </Text>
                </Tabs.Tab>
              );
            })}
          </Tabs.List>
        </ScrollArea>
      </Tabs>
      <Outlet />
    </>
  );
}
