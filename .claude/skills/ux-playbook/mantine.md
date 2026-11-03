# Mantine: versions, API lookup, theme

> **Use when:** you need a Mantine API detail, the right docs for the installed version, or the theming rules.
> **Sources:** Mantine [LLM docs index](https://mantine.dev/llms.txt), [v8 LLM docs index](https://v8.mantine.dev/llms.txt), [Theme object](https://mantine.dev/llms/theming-theme-object.md), [Style props](https://mantine.dev/llms/styles-style-props.md), [Responsive styles](https://mantine.dev/llms/styles-responsive.md), [UI examples](https://ui.mantine.dev).

The playbook decides **what** to use. Mantine's docs decide **how** to call it. Do not guess an API. Look it up.

## Know the version

1. Read `@mantine/core` in the app's `package.json`. All `@mantine/*` packages must have the same version.
2. Use the docs of that major version:
   - **v9**: `https://mantine.dev/llms.txt` → one Markdown page per topic, e.g.
     `https://mantine.dev/llms/core-modal.md`, `https://mantine.dev/llms/x-notifications.md`,
     `https://mantine.dev/llms/form-validation.md`, `https://mantine.dev/llms/hooks-use-disclosure.md`.
   - **v8**: `https://v8.mantine.dev/llms.txt`, with the same page names on that host.
3. Playbook examples are written for v9. A `v8:` line marks a difference that matters.

Page name prefixes: `core-` components, `x-` extensions (modals, notifications, spotlight, nprogress), `form-`,
`hooks-`, `dates-`, `charts-`, `styles-`, `theming-`, `q-` (FAQ).

## Theme first

- **Colours**: use theme colours (`color="red"`, `c="dimmed"`, `bg="var(--mantine-color-body)"`), never hex values in
  components. Status colours belong in one mapping (see [data display](decisions/data-display.md)).
- **Spacing and sizes**: tokens `xs`–`xl` (`gap="md"`, `p="lg"`, `mt="xl"`), never pixel values in components.
- **Radius, shadows, fonts**: set once in the theme (`createTheme`), not per component.
- **Light and dark**: style with CSS variables and `light-dark()`, or `@mixin light` / `@mixin dark` in CSS modules.
  Check both schemes ([visual check](process/visual-check.md)).
- **Default props**: when a component needs the same props everywhere (for example `Table` density), set them in
  the theme's `components` key instead of repeating them.

## Styling, in order of preference

1. Component props (`variant`, `size`, `color`, `radius`).
2. Style props (`mt`, `p`, `c`, `fw`, `w`, `maw`), which also take responsive objects (`{ base: 'xs', sm: 'md' }`).
3. A CSS module next to the component, using Mantine variables and `postcss-preset-mantine` mixins.
4. The `classNames` / `styles` API to style an inner part of a component.

Never use a second UI kit, `!important` against Mantine internals, or inline colours.

## Packages you will meet

| Need | Package | Docs |
| --- | --- | --- |
| Components, layout, inputs | `@mantine/core` | `core-*` |
| Toast notifications | `@mantine/notifications` | `x-notifications` |
| Confirm and multi-step modals | `@mantine/modals` | `x-modals` |
| Forms and validation | `@mantine/form` | `form-*` |
| Command palette | `@mantine/spotlight` | `x-spotlight` |
| Route-change progress bar | `@mantine/nprogress` | `x-nprogress` |
| Dates, pickers | `@mantine/dates` | `dates-*` |
| Charts | `@mantine/charts` | `charts-*` |
| Hooks (media query, disclosure, local storage, network) | `@mantine/hooks` | `hooks-*` |

## Compositions

[ui.mantine.dev](https://ui.mantine.dev) has ready-made compositions (headers, navbars, tables, empty pages). Use one
as a starting point, then apply the playbook's rules. In a product with shared components, use those first.

## Before you add a dependency

If Mantine or the product cannot do something (a guided tour, a virtualised table, a rich-text editor), do not add a
library on your own. Raise a [UX proposal](process/ux-challenge.md) with the need, the options and their cost.
