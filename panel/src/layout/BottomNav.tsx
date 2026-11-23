import { IconActivity, IconLayoutDashboard, IconServer2, IconSettings } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Text } from '@mantine/core';
import { AnchorLink } from '@/components/links';
import classes from './BottomNav.module.css';

const items = [
  { to: '/', label: 'nav.dashboard', icon: IconLayoutDashboard, exact: true },
  { to: '/servers', label: 'nav.servers', icon: IconServer2, exact: false },
  { to: '/activity', label: 'nav.activity', icon: IconActivity, exact: true },
  { to: '/system', label: 'nav.system', icon: IconSettings, exact: false },
] as const;

/** The four top-level destinations on phones (ADR-0035): visible, icon plus label, one tap each. */
export function BottomNav() {
  const { t } = useTranslation();
  return (
    <nav className={classes.bar} aria-label={t('nav.sections')}>
      {items.map(({ to, label, icon: Icon, exact }) => (
        <AnchorLink
          key={to}
          to={to}
          activeOptions={{ exact }}
          activeProps={{ 'data-active': true }}
          className={classes.item}
          c="dimmed"
          underline="never"
        >
          <Icon size={22} stroke={1.75} aria-hidden />
          <Text component="span" size="xs" inherit>
            {t(label)}
          </Text>
        </AnchorLink>
      ))}
    </nav>
  );
}
