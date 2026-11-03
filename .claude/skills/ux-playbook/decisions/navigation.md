# Navigation: choosing the component

> **Use when:** the user moves between places: the app's main areas, sections of an object, steps of a task, levels of a hierarchy, pages of a long list; or you must pick a link or a button.
> **Sources:** Primer [Navigation](https://primer.style/product/ui-patterns/navigation/), [Layout](https://primer.style/product/getting-started/foundations/layout/), [UnderlineNav guidelines](https://primer.style/product/components/underline-nav/guidelines/), [Breadcrumbs guidelines](https://primer.style/product/components/breadcrumbs/guidelines/), [Pagination guidelines](https://primer.style/product/components/pagination/guidelines/), [Link guidelines](https://primer.style/product/components/link/guidelines/), [PageHeader guidelines](https://primer.style/product/components/page-header/guidelines/), [Links and buttons](https://primer.style/accessibility/design-guidance/links-and-buttons/) · Mantine [AppShell](https://mantine.dev/llms/core-app-shell.md), [NavLink](https://mantine.dev/llms/core-nav-link.md), [Burger](https://mantine.dev/llms/core-burger.md), [Tabs](https://mantine.dev/llms/core-tabs.md), [Scroller](https://mantine.dev/llms/core-scroller.md), [Stepper](https://mantine.dev/llms/core-stepper.md), [Breadcrumbs](https://mantine.dev/llms/core-breadcrumbs.md), [Pagination](https://mantine.dev/llms/core-pagination.md), [Anchor](https://mantine.dev/llms/core-anchor.md), [Spotlight](https://mantine.dev/llms/x-spotlight.md)

## Decide

| Situation | Use | Not | Why |
| --- | --- | --- | --- |
| Move between the app's main areas (global) | `NavLink` items in `AppShell.Navbar` | `Tabs` or a `Menu` at the top level | Always visible, shows where you are, scales to more items |
| Sections of one area or object, more than about 6, or nested (settings) | A `NavLink` list in a side column (split layout) | A long row of tabs | Vertical lists scale; tabs overflow |
| 2 to about 6 peer views of one object (Overview, Logs, Settings) | `Tabs`, active tab in the URL | `Stepper`; stacked tab rows | Free order, one context |
| Steps that must happen in order (setup, checkout) | `Stepper` with Back and Next buttons | `Tabs` | Shows progress and order; blocks skipping |
| Deep in a hierarchy (three or more levels) | `Breadcrumbs` of `Anchor`s above the page title | Breadcrumbs on a top-level page | Shows the path; each level is one click |
| One level below a parent page | A back link (`Anchor` with `IconArrowLeft`) above the title | Only the browser back button | A clear way up |
| A long collection where position matters (tables, search results) | `Pagination` under the list, page in the URL | Infinite scroll | Stable positions; shareable; the footer stays reachable |
| A feed where only recent items matter | A "Load more" `Button` under the list | `Pagination` | Simple; keeps what is loaded |
| The control goes somewhere | `Anchor`, a router link, `NavLink`, or `Button component="a"` | `Button` with `onClick={navigate}` | New tab, copy URL, history all work |
| The control does something | `Button` or `ActionIcon`: see [actions](actions.md) | `Anchor` | Acting is not navigating |
| Jump to any page or command from the keyboard | `Spotlight` (`@mantine/spotlight`), as an extra | The only way to reach a page | Fast for experts, invisible to others |
| Change how the same data is shown (List / Board) | `SegmentedControl`: see [selection](selection.md) | `Tabs` | It is a view option, not a place |

## Rules

1. The user always knows where they are: the current `NavLink` is `active`, the page has a title, the current tab is
   selected, breadcrumbs end with the current page.
2. Every navigation changes the URL: nav items, tabs that switch views, breadcrumbs, pagination. The browser back
   button returns to the previous view.
3. Global navigation lives in `AppShell.Navbar`: up to about 7 top-level items, each with an icon and a text label,
   in a stable order. Set `active` from the current route (a router link inside `renderRoot` sets it through
   `aria-current`). Group with labelled sections when there are more.
4. On phones the navbar collapses at its `breakpoint`; a `Burger` with `hiddenFrom` the same breakpoint and
   `aria-label="Toggle navigation"` opens it. Close the navbar when the user picks a link.
5. Use `Tabs` for peer views of one object: preselect one, put the list directly above the content it switches,
   label each tab with its view name (an icon may help; never an icon alone). Keep the active tab in the URL:
   `value` comes from the route, `onChange` navigates.
6. Never use tabs for sequential steps. Never stack two rows of tabs; use at most two levels of hierarchy and switch
   the outer level to a `NavLink` list. More than about 6 tabs means a `NavLink` list.
7. On phones the tab list scrolls sideways instead of wrapping: put the tabs inside a `Scroller` in `Tabs.List`.
8. Use `Stepper` only for ordered steps. Show Back (`variant="default"`) and Next (primary) under the step content,
   validate a step before Next, keep entered data when going Back, and name the last button after the outcome
   ("Create server"). Set `allowNextStepsSelect={false}` so users cannot skip ahead. On phones use
   `orientation="vertical"`. More than 5 steps: simplify the flow first.
9. Use `Breadcrumbs` only three or more levels deep. Every item is a link except the last (the current page, plain
   text with `aria-current="page"`). Wrap it in `<nav aria-label="Breadcrumb">`. On phones a back link to the parent
   may replace it.
10. `Pagination` goes directly under the collection, keeps the page in the URL (`?page=3`), shows the total
    ("1–20 of 230") and uses `hideWithOnePage`. Keep the page size stable; 20 rows is a good start for tables.
    On narrow widths use `layout="responsive"`.
11. "Load more" appends items, says how many are shown ("40 of 230") and moves focus to the first new item. Avoid
    infinite scroll when the page has a footer or users must find an item again.
12. A link navigates; a button acts. Underline links in running text (`Anchor underline="always"`). Do not open links
    in a new tab by default. Never disable a link: remove it, or explain why the place is not available.
13. Never navigate or open a menu on hover. `Menu` keeps the default click trigger (`"click-hover"` if hover is wanted).
14. Before leaving a page with unsaved changes, follow [saving](../patterns/saving.md).
15. After a client-side navigation, move focus to the new `h1` and update `document.title`:
    [accessibility](../accessibility.md).
16. `Spotlight` is an accelerator only. Every page and action in it must also be reachable through visible UI. Show the
    shortcut on a visible search button ("Search  Ctrl K").

## Recipes

### Global navigation item

```tsx
import { NavLink } from '@mantine/core';
import { IconServer } from '@tabler/icons-react';

<NavLink
  href="/servers"
  label="Servers"
  leftSection={<IconServer size={18} />}
  active={currentPath.startsWith('/servers')}
  onClick={closeMobileNavbar}
/>
```

### Tabs kept in the URL, scrolling on phones

```tsx
import { Scroller, Tabs } from '@mantine/core';

<Tabs value={tabFromUrl} onChange={(value) => value && goToTab(value)}>
  <Tabs.List aria-label="Server sections">
    <Scroller>
      <Tabs.Tab value="overview">Overview</Tabs.Tab>
      <Tabs.Tab value="logs">Logs</Tabs.Tab>
      <Tabs.Tab value="backups">Backups</Tabs.Tab>
      <Tabs.Tab value="settings">Settings</Tabs.Tab>
    </Scroller>
  </Tabs.List>
  <Tabs.Panel value="overview" pt="md">{/* … */}</Tabs.Panel>
</Tabs>
```

### Breadcrumbs

```tsx
import { Anchor, Breadcrumbs, Text } from '@mantine/core';

<nav aria-label="Breadcrumb">
  <Breadcrumbs>
    <Anchor href="/projects">Projects</Anchor>
    <Anchor href="/projects/atlas">Atlas</Anchor>
    <Text aria-current="page">Server web-01</Text>
  </Breadcrumbs>
</nav>
```

### Stepper with Back and Next

```tsx
import { Button, Group, Stepper } from '@mantine/core';

<>
  <Stepper active={active} allowNextStepsSelect={false}>
    <Stepper.Step label="Choose game">{/* step content */}</Stepper.Step>
    <Stepper.Step label="Settings">{/* step content */}</Stepper.Step>
    <Stepper.Step label="Review">{/* step content */}</Stepper.Step>
  </Stepper>
  <Group justify="flex-end" gap="sm" mt="xl">
    {active > 0 && <Button variant="default" onClick={back}>Back</Button>}
    <Button onClick={next}>{active === 2 ? 'Create server' : 'Next'}</Button>
  </Group>
</>
```

v8: there is no `Scroller` (keep tab labels short so the list fits) and no `Pagination layout="responsive"`.

## Pitfalls

- Tabs used as a wizard ("Step 1", "Step 2"). Fix: `Stepper`.
- The active tab lives only in state; reload or a shared link opens the wrong tab. Fix: keep it in the URL.
- A `Button` that calls `navigate()`, or an `Anchor` with `href="#"` that saves. Fix: link to go, button to act.
- Breadcrumbs on a top-level page, or a last crumb that links to itself. Fix: three levels or more; last item is text.
- Infinite scroll on a list with a footer, or where users come back to an item. Fix: `Pagination` or "Load more".
- The mobile navbar stays open over the new page after a tap. Fix: close it on link click.
- A navbar with 15 flat items. Fix: group into labelled sections, or move sub-sections into the area's own nav.
- Commands reachable only through `Spotlight`. Fix: a visible path for each one.

## Checklist

- [ ] The current place is visible: active nav item, page title, selected tab or step.
- [ ] Every navigation changes the URL and the back button works.
- [ ] Links navigate and buttons act; no `onClick` navigation on a `Button`.
- [ ] Tabs switch peer views only, sit directly above their content, and scroll on phones.
- [ ] Steppers have Back and Next, validate each step and keep data when going back.
- [ ] Breadcrumbs appear only three or more levels deep and end with the current page as text.
- [ ] Pagination sits under the list, keeps the page in the URL and shows the total.
- [ ] On phones the navbar opens from a labelled `Burger` and closes after a choice.
