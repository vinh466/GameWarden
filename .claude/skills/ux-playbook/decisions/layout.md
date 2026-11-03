# Layout: choosing the component

> **Use when:** you build or change a page's structure: the app frame, the page header, sections, widths, spacing, density, columns, or how the page adapts to a phone.
> **Sources:** Primer [Layout](https://primer.style/product/getting-started/foundations/layout/), [Responsive](https://primer.style/product/getting-started/foundations/responsive/), [Typography](https://primer.style/product/getting-started/foundations/typography/), [PageHeader guidelines](https://primer.style/product/components/page-header/guidelines/), [PageHeader accessibility](https://primer.style/product/components/page-header/accessibility/), [PageLayout accessibility](https://primer.style/product/components/page-layout/accessibility/), [Card accessibility](https://primer.style/product/components/card/accessibility/), [Navigation](https://primer.style/product/ui-patterns/navigation/) · Mantine [AppShell](https://mantine.dev/llms/core-app-shell.md), [Container](https://mantine.dev/llms/core-container.md), [Stack](https://mantine.dev/llms/core-stack.md), [Group](https://mantine.dev/llms/core-group.md), [SimpleGrid](https://mantine.dev/llms/core-simple-grid.md), [Grid](https://mantine.dev/llms/core-grid.md), [Card](https://mantine.dev/llms/core-card.md), [Paper](https://mantine.dev/llms/core-paper.md), [Divider](https://mantine.dev/llms/core-divider.md), [Title](https://mantine.dev/llms/core-title.md), [Burger](https://mantine.dev/llms/core-burger.md), [Responsive styles](https://mantine.dev/llms/styles-responsive.md), [useMediaQuery](https://mantine.dev/llms/hooks-use-media-query.md)

## Decide

| Situation | Use | Not | Why |
| --- | --- | --- | --- |
| The app frame: top bar, side navigation, content | `AppShell` with `header`, `navbar`, `AppShell.Main` | Hand-made fixed `div`s | Landmarks (`header`, `nav`, `main`) and phone collapse come built in |
| Top of every page: title, status, main actions | A page header: `Group justify="space-between"` with `Title order={1}` on the left, actions on the right | Actions spread through the body | Users look top-left for "where am I", top-right for "what can I do" |
| Form, settings, reading text | `Container size="sm"` (or `"md"`) | Full-width inputs and paragraphs | Long lines are hard to read; keep text near 80 characters per line |
| Table, board, dashboard | The full content width (`Container fluid` or no container) | A narrow container | Columns need the width |
| Sign-in, one-off step, "working…" page | `Container size="xs"`, centred | A full page layout | One task, nothing else on screen |
| Sections of one page | Space plus a section `Title order={2}` | A `Card` around every section | Boxes everywhere add noise and nest badly |
| A light break inside a section (before a footer, between groups) | `Divider` | A `Card` | Lowest visual weight that still separates |
| One self-contained object among many, or a region that must read as one unit | `Card withBorder` or `Paper withBorder p="md"` | A `Divider` | The border says "these belong together" |
| Vertical rhythm | `Stack gap="…"` | `<br />`, margins in px | Spacing comes from theme tokens |
| Items in a row (toolbar, buttons, title + badge) | `Group gap="…"` | Inline styles | Wraps and aligns consistently |
| Equal tiles (stat cards, card grid) | `SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}` | `Grid` with manual spans | One prop sets columns per breakpoint |
| Unequal columns (content + side panel) | `Grid` with `Grid.Col span={{ base: 12, md: 8 }}` | `SimpleGrid` | Spans differ per column |
| List + detail (settings sections, inbox) | A split layout: list on the left, detail on the right; one at a time on phones | Both squeezed side by side on a phone | See rule 12 |

## Rules

1. Give every page one main job. Its title names it and its primary action sits in the page header.
2. Order a page top to bottom: back link or `Breadcrumbs` (if any) → page header → `Tabs` (if any) → content.
   See [navigation](navigation.md).
3. Page header: title, then status (`Badge`) and an optional one-line description (`Text c="dimmed"`) on the left;
   actions on the right, primary last. At most one primary action: see [actions](actions.md).
4. Use one `h1` per page: the page title, `Title order={1}`. Sections are `order={2}`, subsections `order={3}`.
   Never skip a level. Change the look with `size` (`Title order={2} size="h4"`), never by picking another level.
5. Build hierarchy with size, weight, position and space. Do not use colour as the main way to emphasise text.
6. Group by distance: related items close (`gap="xs"` or `"sm"`), sections far apart (`gap="xl"`).
   Use theme tokens only (`xs`–`xl` in `gap`, `p`, `m`, `spacing`). No pixel values, no inline margins.
7. Match density to the job. Overview and landing pages: generous (`gap="lg"`–`"xl"`, `p="lg"`). Working views with
   much data: compact (`gap="sm"`, `Table verticalSpacing="xs"`). Keep one density inside a region.
8. Use `Card` for items in a collection or one standalone object. Never wrap the whole page in a `Card`, never nest
   cards. Give each card a heading. When a card holds several controls, the controls get hover and focus, not the card.
9. Put the frame in `AppShell`: set `navbar={{ width, breakpoint: 'sm', collapsed: { mobile: !opened } }}` and a
   `Burger` with `hiddenFrom="sm"` and `aria-label`. Page actions never go in `AppShell.Header`; the header holds
   global things (logo, search, account). Do not add a `<main>` inside `AppShell.Main`.
10. Support widths from 320 px up without losing any function. On a phone, change position, never remove capability.
11. On phones: one column; the page header wraps (title first, actions below); keep the primary action visible and
    fold secondary actions into a "More" `Menu`. Switch with `hiddenFrom` / `visibleFrom`.
12. Split layouts on phones show the list **or** the detail. The detail view gets a back link to the list.
    A filter sidebar becomes a `Menu` (few options) or a `Drawer` (many). A metadata side panel stacks below the
    content; a short summary may sit above it.
13. Prefer `hiddenFrom` / `visibleFrom` and responsive style props (`cols={{ base: 1, sm: 2 }}`): they are CSS and safe
    with server rendering. Use `useMediaQuery` only for props of things rendered on the client, such as
    `Modal fullScreen`.
14. Make touch targets at least 24 × 24 px and aim for 44 × 44 px on phones: avoid `size="xs"` buttons and icons as
    the main touch targets.
15. Check every page at phone and desktop width, light and dark: [visual check](../process/visual-check.md).

## Recipes

### App frame

```tsx
import { AppShell, Burger, Group, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import type { ReactNode } from 'react';

export function Shell({ children }: { children: ReactNode }) {
  const [opened, { toggle }] = useDisclosure();
  return (
    <AppShell
      padding="md"
      header={{ height: 60 }}
      navbar={{ width: 260, breakpoint: 'sm', collapsed: { mobile: !opened } }}
    >
      <AppShell.Header>
        <Group h="100%" px="md">
          <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Toggle navigation" />
          <Text fw={700}>Acme</Text>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="md">{/* NavLink items, see navigation.md */}</AppShell.Navbar>
      <AppShell.Main>{children}</AppShell.Main>
    </AppShell>
  );
}
```

### Page header that folds on phones

```tsx
import { ActionIcon, Badge, Button, Group, Menu, Stack, Text, Title } from '@mantine/core';
import { IconDots, IconPlus } from '@tabler/icons-react';

<Group justify="space-between" align="flex-start" wrap="wrap" gap="sm">
  <Stack gap="xs">
    <Group gap="sm">
      <Title order={1}>Servers</Title>
      <Badge variant="light" color="green">3 running</Badge>
    </Group>
    <Text c="dimmed" size="sm">Game servers in this workspace.</Text>
  </Stack>
  <Group gap="sm">
    <Button variant="default" visibleFrom="sm">Import</Button>
    <Menu position="bottom-end">
      <Menu.Target>
        <ActionIcon variant="default" size="lg" aria-label="More page actions" hiddenFrom="sm">
          <IconDots size={18} />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Item>Import</Menu.Item>
      </Menu.Dropdown>
    </Menu>
    <Button leftSection={<IconPlus size={16} />}>Create server</Button>
  </Group>
</Group>
```

### Narrow form page

```tsx
import { Container, Stack, Title } from '@mantine/core';

<Container size="sm" py="lg">
  <Stack gap="xl">
    <Title order={1}>Account settings</Title>
    {/* sections: Title order={2} + fields in Stack gap="md" */}
  </Stack>
</Container>
```

v8: `Grid` uses `gutter` instead of `gap`.

## Pitfalls

- Every section boxed in a `Card`, cards inside cards. Fix: space and section titles; a `Card` only for an object.
- Two `h1`s, or a section heading chosen for its size. Fix: one `h1`; set the look with `Title size`.
- A form stretched across a 1920 px screen. Fix: `Container size="sm"`.
- `mt={13}`, `style={{ marginTop: 13 }}`. Fix: theme tokens (`mt="sm"`).
- Secondary actions simply disappear on phones. Fix: move them into a "More" `Menu`, shown with `hiddenFrom`.
- List and detail side by side at 390 px. Fix: one at a time, with a back link.
- `useMediaQuery` to hide content in a server-rendered app, which causes a flash or hydration mismatch.
  Fix: `hiddenFrom` / `visibleFrom`.
- Page actions placed in the global `AppShell.Header`. Fix: put them in the page header.

## Checklist

- [ ] The frame uses `AppShell`; the navbar collapses behind a labelled `Burger` on phones.
- [ ] One `h1` (the page title); heading levels never skip.
- [ ] The page header shows title (and status) on the left and actions on the right, with one primary action.
- [ ] Forms and text use a narrow `Container`; tables and dashboards use the full width.
- [ ] All spacing uses theme tokens; related items sit closer than unrelated ones.
- [ ] No nested cards; no card wraps the whole page.
- [ ] At 390 px wide: one column, nothing overflows, no function is lost, secondary actions are in a menu.
- [ ] Split layouts show list or detail on phones, and the detail has a way back.
