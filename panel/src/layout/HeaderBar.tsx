import {
  IconCheck,
  IconCloudDownload,
  IconDeviceDesktop,
  IconLanguage,
  IconListCheck,
  IconMoon,
  IconSearch,
  IconSettings,
  IconSun,
} from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  ActionIcon,
  Badge,
  Burger,
  Button,
  Group,
  Indicator,
  Kbd,
  Menu,
  Text,
  Tooltip,
  useMantineColorScheme,
  type MantineColorScheme,
} from '@mantine/core';
import { spotlight } from '@mantine/spotlight';
import { queries } from '@/api/queries';
import { useRealtimeStatus } from '@/api/realtimeStatus';
import { ActionIconLink } from '@/components/links';
import {
  isUpdateActive,
  isUpdateBannerVisible,
  openUpdateDialog,
  useUpdateState,
} from '@/features/system/update/updateStore';
import { useUpdateStatus } from '@/features/system/update/useUpdateStatus';
import { languages } from '@/i18n';
import { AccountMenu } from './AccountMenu';
import { Logo } from './Logo';

export function HeaderBar({
  mobileOpened,
  desktopOpened,
  onToggleMobile,
  onToggleDesktop,
}: {
  mobileOpened: boolean;
  desktopOpened: boolean;
  onToggleMobile: () => void;
  onToggleDesktop: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Group h="100%" px="md" justify="space-between" wrap="nowrap" gap="xs">
      <Group gap="sm" wrap="nowrap">
        <Burger
          opened={mobileOpened}
          onClick={onToggleMobile}
          hiddenFrom="sm"
          size="sm"
          aria-label={t('header.toggleNavigation')}
        />
        <Burger
          opened={desktopOpened}
          onClick={onToggleDesktop}
          visibleFrom="sm"
          size="sm"
          aria-label={t('header.toggleNavigation')}
        />
        <Logo />
        <CoreStatus />
        <UpdateAvailable />
      </Group>
      <Group gap={6} wrap="nowrap">
        <Button
          variant="default"
          leftSection={<IconSearch size={16} />}
          rightSection={<Kbd size="xs">Ctrl K</Kbd>}
          onClick={spotlight.open}
          visibleFrom="md"
          w={240}
          justify="space-between"
          c="dimmed"
          fw={400}
        >
          {t('header.search')}
        </Button>
        <ActionIcon
          size={40}
          onClick={spotlight.open}
          hiddenFrom="md"
          aria-label={t('header.search')}
        >
          <IconSearch size={18} />
        </ActionIcon>
        <JobsIndicator />
        <PreferencesMenu />
        <AccountMenu />
      </Group>
    </Group>
  );
}

/** "● v0.2.0" (docs/07 §8): green while the realtime link is up, yellow while it reconnects, red when it is down. */
function CoreStatus() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());
  const link = useRealtimeStatus();
  const state =
    link === 'off'
      ? system.isError
        ? 'down'
        : 'up'
      : link === 'connected'
        ? 'up'
        : link === 'disconnected'
          ? 'down'
          : 'retrying';
  const label = {
    up: t('core.connected'),
    retrying: t('core.reconnecting'),
    down: t('core.disconnected'),
  }[state];
  return (
    <Badge
      visibleFrom="xs"
      variant="dot"
      color={{ up: 'green', retrying: 'yellow', down: 'red' }[state]}
      size="lg"
      tt="none"
      fw={500}
      title={label}
    >
      {system.data && state === 'up' ? t('core.version', { version: system.data.version }) : label}
    </Badge>
  );
}

/** docs/11 §2: a newer release opens the one update dialog from anywhere, including a phone. */
function UpdateAvailable() {
  const { t } = useTranslation();
  const { status, system } = useUpdateStatus();
  const update = useUpdateState();
  if (
    isUpdateActive(update) ||
    isUpdateBannerVisible(update) ||
    !system?.installRoot ||
    !status?.available ||
    !status.latest
  ) {
    return null;
  }

  const label = t('core.updateAvailable', { version: status.latest });
  return (
    <>
      <Tooltip label={t('system.updateHint')} events={{ hover: true, focus: true, touch: false }}>
        <Button
          visibleFrom="sm"
          variant="light"
          color="violet"
          size="xs"
          leftSection={<IconCloudDownload size={14} />}
          onClick={openUpdateDialog}
        >
          {label}
        </Button>
      </Tooltip>
      <Tooltip label={label} events={{ hover: true, focus: true, touch: false }}>
        <ActionIcon
          hiddenFrom="sm"
          variant="light"
          color="violet"
          size="lg"
          aria-label={label}
          onClick={openUpdateDialog}
        >
          <IconCloudDownload size={18} />
        </ActionIcon>
      </Tooltip>
    </>
  );
}

function JobsIndicator() {
  const { t } = useTranslation();
  const jobs = useQuery(queries.jobs());
  const running =
    jobs.data?.filter((job) => job.state === 'running' || job.state === 'queued').length ?? 0;
  const label =
    running > 0 ? t('header.jobsRunning', { count: running }) : t('header.noJobsRunning');
  return (
    <Tooltip label={label}>
      <Indicator label={running} size={16} disabled={running === 0} color="yellow" offset={4}>
        <ActionIconLink size={40} to="/system/jobs" aria-label={label}>
          <IconListCheck size={18} />
        </ActionIconLink>
      </Indicator>
    </Tooltip>
  );
}

function PreferencesMenu() {
  const { t, i18n } = useTranslation();
  const { colorScheme, setColorScheme } = useMantineColorScheme();
  const schemes: { value: MantineColorScheme; label: string; icon: typeof IconSun }[] = [
    { value: 'light', label: t('header.light'), icon: IconSun },
    { value: 'dark', label: t('header.dark'), icon: IconMoon },
    { value: 'auto', label: t('header.auto'), icon: IconDeviceDesktop },
  ];
  return (
    <Menu position="bottom-end" width={220}>
      <Menu.Target>
        <ActionIcon size={40} aria-label={`${t('header.colorScheme')}, ${t('header.language')}`}>
          <IconSettings size={18} />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{t('header.colorScheme')}</Menu.Label>
        {schemes.map(({ value, label, icon: Icon }) => (
          <Menu.Item
            key={value}
            leftSection={<Icon size={16} />}
            rightSection={colorScheme === value && <IconCheck size={14} />}
            onClick={() => setColorScheme(value)}
          >
            {label}
          </Menu.Item>
        ))}
        <Menu.Divider />
        <Menu.Label>
          <Group gap={6}>
            <IconLanguage size={14} />
            <Text size="xs" inherit>
              {t('header.language')}
            </Text>
          </Group>
        </Menu.Label>
        {languages.map((language) => (
          <Menu.Item
            key={language.code}
            rightSection={i18n.resolvedLanguage === language.code && <IconCheck size={14} />}
            onClick={() => void i18n.changeLanguage(language.code)}
          >
            {language.label}
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  );
}
