import {
  ActionIcon,
  Badge,
  Card,
  createTheme,
  darken,
  Loader,
  Menu,
  Modal,
  NavLink,
  Paper,
  Table,
  Tooltip,
  type CSSVariablesResolver,
  type MantineColor,
} from '@mantine/core';

// docs/11 §5: violet primary (never a status colour), Inter and JetBrains Mono (self-hosted), radius md.
const sans =
  "'Inter Variable', Inter, system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif";
const mono =
  "'JetBrains Mono Variable', 'JetBrains Mono', ui-monospace, 'Cascadia Mono', Consolas, monospace";

export const theme = createTheme({
  primaryColor: 'violet',
  // Dark: shade 7, so white text on filled buttons keeps 4.5:1 (docs/11 §10 rule 8; axe pass 2026-10-28).
  primaryShade: { light: 6, dark: 7 },
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

/**
 * Text contrast of at least 4.5:1 (WCAG AA, docs/11 §10 rule 8). Mantine's defaults miss it for dimmed text
 * (#868e96 on white: 3.3:1), red text, the text of light badges (green 3.8:1, yellow 2.7:1) and, in dark mode,
 * dimmed text and links. Checked with axe-core on every page in both schemes.
 */
export const cssVariablesResolver: CSSVariablesResolver = (t) => ({
  variables: {},
  light: {
    '--mantine-color-dimmed': '#646b73',
    '--mantine-color-red-text': t.colors.red[9],
    '--mantine-color-gray-outline': '#646b73',
    ...Object.fromEntries(
      (['green', 'yellow', 'orange', 'red', 'blue', 'gray', 'violet'] as const).map((color) => [
        `--mantine-color-${color}-light-color`,
        darken(t.colors[color][9], color === 'yellow' || color === 'orange' ? 0.35 : 0.25),
      ])
    ),
  },
  dark: {
    '--mantine-color-dimmed': '#9a9a9a',
    '--mantine-color-anchor': t.colors.violet[3],
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
