import {
  ActionIcon,
  Badge,
  Card,
  createTheme,
  Loader,
  Menu,
  Modal,
  NavLink,
  Paper,
  Table,
  Tooltip,
  type MantineColor,
} from '@mantine/core';

// docs/11 §5: violet primary (never a status colour), Inter and JetBrains Mono (self-hosted), radius md.
const sans =
  "'Inter Variable', Inter, system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif";
const mono =
  "'JetBrains Mono Variable', 'JetBrains Mono', ui-monospace, 'Cascadia Mono', Consolas, monospace";

export const theme = createTheme({
  primaryColor: 'violet',
  primaryShade: { light: 6, dark: 5 },
  fontFamily: sans,
  fontFamilyMonospace: mono,
  headings: { fontFamily: sans },
  defaultRadius: 'md',
  cursorType: 'pointer',
  components: {
    ActionIcon: ActionIcon.extend({ defaultProps: { variant: 'subtle', color: 'gray' } }),
    Badge: Badge.extend({ defaultProps: { variant: 'light', radius: 'sm' } }),
    Card: Card.extend({ defaultProps: { withBorder: true, padding: 'lg' } }),
    Loader: Loader.extend({ defaultProps: { type: 'dots' } }),
    Menu: Menu.extend({ defaultProps: { shadow: 'md', withinPortal: true } }),
    Modal: Modal.extend({ defaultProps: { centered: true } }),
    NavLink: NavLink.extend({ defaultProps: { variant: 'light' } }),
    Paper: Paper.extend({ defaultProps: { withBorder: true } }),
    Table: Table.extend({ defaultProps: { verticalSpacing: 'xs', highlightOnHover: true } }),
    Tooltip: Tooltip.extend({ defaultProps: { withArrow: true, openDelay: 300 } }),
  },
});

/** Status colours of docs/11 §5; one place decides them (see StatusBadge). */
export const statusColors = {
  running: 'green',
  transitional: 'blue',
  failed: 'red',
  idle: 'gray',
  busy: 'yellow',
} satisfies Record<string, MantineColor>;
