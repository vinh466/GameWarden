# Data display: choosing the component

> **Use when:** you show a collection or the details of records: tables, card grids, lists, timelines, status labels, dates, long text, missing values, row actions, sorting and filtering.
> **Sources:** Primer [DataTable guidelines](https://primer.style/product/components/data-table/guidelines/), [DataTable accessibility](https://primer.style/product/components/data-table/accessibility/), [RelativeTime guidelines](https://primer.style/product/components/relative-time/guidelines/), [RelativeTime accessibility](https://primer.style/product/components/relative-time/accessibility/), [Truncate accessibility](https://primer.style/product/components/truncate/accessibility/), [Card accessibility](https://primer.style/product/components/card/accessibility/), [Pagination guidelines](https://primer.style/product/components/pagination/guidelines/) · Mantine [Table](https://mantine.dev/llms/core-table.md), [Badge](https://mantine.dev/llms/core-badge.md), [Text](https://mantine.dev/llms/core-text.md), [Card](https://mantine.dev/llms/core-card.md), [SimpleGrid](https://mantine.dev/llms/core-simple-grid.md), [List](https://mantine.dev/llms/core-list.md), [Timeline](https://mantine.dev/llms/core-timeline.md), [Menu](https://mantine.dev/llms/core-menu.md), [ActionIcon](https://mantine.dev/llms/core-action-icon.md), [UnstyledButton](https://mantine.dev/llms/core-unstyled-button.md), [VisuallyHidden](https://mantine.dev/llms/core-visually-hidden.md), [Spoiler](https://mantine.dev/llms/core-spoiler.md)

## Decide

| Situation | Use | Not | Why |
| --- | --- | --- | --- |
| Many records with the same fields, to scan, compare, sort | `Table` | A card grid | Columns line values up for comparison |
| A few records (about 12 or fewer) with a picture or a rich summary, to browse | `SimpleGrid` of `Card`s | `Table` | Cards show variety; tables show sameness |
| A simple list: one line per item, a name or a link | `List`, or a `Stack` of rows | A one-column `Table` | No columns to compare |
| Events in time order (activity, history, audit) | `Timeline` | `Table` | Order and time are the point |
| The fields of one record | `Table variant="vertical"` (label + value rows) | A grid of loose `Text` | Labels and values stay paired for screen readers |
| Data with parents and children | A nested `List` or Mantine `Tree` | A flat `Table` | Tables suit a flat structure |
| Cells that mostly stay empty, or long paragraphs per item | A list of cards or rows | `Table` | Empty and long cells make a table unreadable |

## Rules

1. Put the identifying column first (the name) and make it the link to the record. Keep columns few: scanning many
   rows is easier than scanning many columns. One kind of data per column; short header labels with the unit
   ("Size (GB)").
2. Right-align numbers in both `Table.Th` and `Table.Td` (`ta="right"`) and set `tabularNums` on the `Table`. Use the
   same precision in a column. Left-align text. Use one date format per column.
3. Give each table an accessible name: a `Table.Caption`, or `aria-labelledby` pointing at the section title.
   Do not repeat the page title as a visible caption.
4. Density: `verticalSpacing="xs"` for working tables with many rows, `"sm"` for most tables, `"md"` for a few rows
   on an overview. Add `highlightOnHover` when rows are clickable; `stickyHeader` for long tables.
5. Responsive: wrap every table in `Table.ScrollContainer minWidth={…}` so it scrolls instead of breaking the page.
   At narrow widths, hide less important columns (`visibleFrom="md"` on both their `Th` and `Td`). When phone use
   matters and each record has few key fields, show a card list `hiddenFrom="sm"` and the table `visibleFrom="sm"`.
6. Status is a `Badge` with a colour **and** a word (and an icon in `leftSection` when it helps). Never a coloured dot
   alone. Use one status-to-colour map for the whole app, matching the severity colours in [feedback](feedback.md).
7. Dates: relative ("3 hours ago") for recent activity; exact ("Wed, 4 Nov 2026, 14:05") for deadlines, schedules,
   future dates and audit logs. Write long forms ("2 months ago", not "2mo"). Format with `Intl.RelativeTimeFormat`
   and `Intl.DateTimeFormat` in the user's locale. Use a `<time dateTime>` element.
8. A relative date must also offer the exact one to everyone: a `title` covers mouse users only, so also show the
   exact time in the record's detail view, or, when the date is a link, add a `Tooltip` with focus events.
9. Prefer wrapping to truncating. Truncate only where width is fixed (`Text truncate="end"` inside a width-limited
   cell; `lineClamp={2}` for descriptions). The full text must be reachable by everyone: on the detail page, through
   a `Spoiler` ("Show more"), or in a `Tooltip` on the focusable link. Never truncate links or buttons.
10. Missing values: when the gap means something, write it ("Never", "Not set", "None"); otherwise leave the cell
    blank. Never a bare "—", "-" or "x": screen readers read it as "dash" or skip it. If the product already uses
    a dash, hide it from screen readers and add `VisuallyHidden` text ("Not set").
11. Row actions: the row's main action is the link on its name. Put other actions in a `Menu` at the row's end,
    behind an `ActionIcon` named with the row ("Actions for web-01"). One frequent action may be a visible
    `ActionIcon`. The action column header is `VisuallyHidden` text ("Actions"). Row actions are always visible,
    never only on hover.
12. Sorting: a sortable header is an `UnstyledButton` inside `Table.Th` with an arrow icon for the direction, and the
    `Th` carries `aria-sort`. Sort by one column at a time. Start in a sensible order: newest first, or A–Z on the
    first column.
13. Filtering: put the filter input and table-wide actions above the table, show the result count ("12 of 230
    servers"), and keep the filter in the URL. No results → [empty states](../patterns/empty-states.md) with
    "Clear filters".
14. Large sets: paginate under the table (start at 20 rows per page): see [navigation](navigation.md). Loading shows
    skeleton rows shaped like the real ones: [loading](../patterns/loading.md). Failure: [feedback](feedback.md).
15. Cards in a collection: each has a heading, the same layout and order of fields, and controls named with the card
    ("More options for web-01"). Hover and focus styles go on the controls, not the whole card.

## Recipes

### Table with status, numbers and a row menu

```tsx
import { ActionIcon, Anchor, Badge, Menu, Table, VisuallyHidden } from '@mantine/core';
import { IconDots } from '@tabler/icons-react';

<Table.ScrollContainer minWidth={640}>
  <Table tabularNums highlightOnHover verticalSpacing="xs" aria-labelledby="servers-title">
    <Table.Thead>
      <Table.Tr>
        <Table.Th>Name</Table.Th>
        <Table.Th>Status</Table.Th>
        <Table.Th ta="right">Players</Table.Th>
        <Table.Th visibleFrom="md">Region</Table.Th>
        <Table.Th><VisuallyHidden>Actions</VisuallyHidden></Table.Th>
      </Table.Tr>
    </Table.Thead>
    <Table.Tbody>
      {servers.map((server) => (
        <Table.Tr key={server.id}>
          <Table.Td><Anchor href={`/servers/${server.id}`}>{server.name}</Anchor></Table.Td>
          <Table.Td><Badge variant="light" color={statusColor[server.status]}>{server.statusLabel}</Badge></Table.Td>
          <Table.Td ta="right">{server.players}</Table.Td>
          <Table.Td visibleFrom="md">{server.region ?? 'Not set'}</Table.Td>
          <Table.Td>
            <Menu position="bottom-end">
              <Menu.Target>
                <ActionIcon variant="subtle" aria-label={`Actions for ${server.name}`}><IconDots size={16} /></ActionIcon>
              </Menu.Target>
              <Menu.Dropdown>{/* Restart, Rename, divider, Delete server */}</Menu.Dropdown>
            </Menu>
          </Table.Td>
        </Table.Tr>
      ))}
    </Table.Tbody>
  </Table>
</Table.ScrollContainer>
```

### Sortable column header

```tsx
import { Group, Table, UnstyledButton } from '@mantine/core';
import { IconArrowDown, IconArrowUp } from '@tabler/icons-react';

<Table.Th aria-sort={sort === 'asc' ? 'ascending' : sort === 'desc' ? 'descending' : 'none'}>
  <UnstyledButton onClick={toggleSort}>
    <Group gap="xs" wrap="nowrap">
      Created
      {sort === 'asc' && <IconArrowUp size={14} aria-hidden />}
      {sort === 'desc' && <IconArrowDown size={14} aria-hidden />}
    </Group>
  </UnstyledButton>
</Table.Th>
```

### Relative date with the exact one on hover

```tsx
import { Text } from '@mantine/core';

<Text component="time" dateTime={createdAt.toISOString()} title={formatExact(createdAt)} size="sm">
  {formatRelative(createdAt)}
</Text>
```

`formatRelative` and `formatExact` wrap `Intl.RelativeTimeFormat` and `Intl.DateTimeFormat`. Show the exact date in
the detail view too (rule 8).

v8: `Spoiler` uses `initialState` instead of `defaultExpanded`.

## Pitfalls

- A card grid of 200 records the user must compare. Fix: a `Table`.
- Status shown as a green or red dot only. Fix: `Badge` with colour and word.
- Numbers left-aligned in proportional digits, so magnitudes do not line up. Fix: `ta="right"` + `tabularNums`.
- A table that pushes the page wider than the phone. Fix: `Table.ScrollContainer`, fewer columns, or a card list.
- Row actions that appear only on hover. Fix: always-visible `ActionIcon` + `Menu`, named with the row.
- "3h" or "2mo" in a narrow column. Fix: long form; give the column room or show the exact date.
- Truncated names with the full text only in a `title`. Fix: a link to the record, or a `Tooltip` on the link.
- Empty cells filled with "—" for screen readers to read as "dash". Fix: a word, or blank (rule 10).

## Checklist

- [ ] The component matches the decision table (table to compare, cards to browse, timeline for events).
- [ ] The first column identifies the record and links to it; numbers are right-aligned with tabular digits.
- [ ] The table has an accessible name and scrolls inside `Table.ScrollContainer` on narrow screens.
- [ ] Every status shows a word, not only a colour, using one app-wide colour map.
- [ ] Relative dates use long forms and the exact date is reachable without a mouse.
- [ ] Truncated text is reachable in full; links and buttons are never truncated.
- [ ] Missing values show a word or a blank, never a bare dash.
- [ ] Row actions sit at the row end in a named `Menu` and are visible without hover.
- [ ] Sortable headers are buttons with a direction icon and `aria-sort`.
- [ ] Large sets paginate; loading, empty and error states exist.
