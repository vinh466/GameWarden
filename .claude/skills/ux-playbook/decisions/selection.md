# Selection: choosing the component

> **Use when:** the user picks one or more values from a set (on/off, one of a few, one of many, several, or free values with suggestions) and you are choosing between `Switch`, `Checkbox`, `Radio`, `SegmentedControl`, `Select`, `MultiSelect`, `TagsInput`, `Chip`, `Autocomplete`, `NativeSelect` or `Combobox`.
> **Sources:** Primer [Forms](https://primer.style/product/ui-patterns/forms/), [Saving](https://primer.style/product/ui-patterns/saving/), [ToggleSwitch](https://primer.style/product/components/toggle-switch/guidelines/), [Checkbox](https://primer.style/product/components/checkbox/), [CheckboxGroup](https://primer.style/product/components/checkbox-group/guidelines/), [RadioGroup](https://primer.style/product/components/radio-group/guidelines/), [SegmentedControl](https://primer.style/product/components/segmented-control/guidelines/), [Select](https://primer.style/product/components/select/guidelines/), [SelectPanel](https://primer.style/product/components/select-panel/guidelines/), [Autocomplete](https://primer.style/product/components/autocomplete/guidelines/), [TextInputWithTokens](https://primer.style/product/components/text-input-with-tokens/guidelines/) · Mantine [Switch](https://mantine.dev/llms/core-switch.md), [Checkbox](https://mantine.dev/llms/core-checkbox.md), [Radio](https://mantine.dev/llms/core-radio.md), [SegmentedControl](https://mantine.dev/llms/core-segmented-control.md), [SegmentedControl without value](https://mantine.dev/llms/q-segmented-control-no-value.md), [Select](https://mantine.dev/llms/core-select.md), [MultiSelect](https://mantine.dev/llms/core-multi-select.md), [TagsInput](https://mantine.dev/llms/core-tags-input.md), [Chip](https://mantine.dev/llms/core-chip.md), [Autocomplete](https://mantine.dev/llms/core-autocomplete.md), [Select vs Autocomplete](https://mantine.dev/llms/q-select-autocomplete-difference.md), [NativeSelect](https://mantine.dev/llms/core-native-select.md), [Combobox](https://mantine.dev/llms/core-combobox.md)

## Decide

First ask: **does the choice take effect at once, or when the user presses Save/Submit?** Then: one or many,
how many options, and may the user type a value that is not in the list?

| Situation | Use | Not | Why |
| --- | --- | --- | --- |
| One on/off setting that applies at once | `Switch` | `Checkbox` + Save | Works like a light switch; no Save |
| One opt-in inside a form that has a submit button ("Email me updates", "I accept the terms") | `Checkbox` | `Switch` | Saved with the form |
| Several independent on/off choices in a form | `Checkbox.Group` | Several `Switch`es in the form | One label, one error, one submit |
| One of 2–6 options, all visible, inside a form | `Radio.Group` | `Select` | Options can be compared at a glance |
| One of 2–5 short options that changes the current view at once (view mode, sort, format, filter) | `SegmentedControl` | `Radio.Group` + Apply | Applies instantly, always has a value |
| One of about 7 or more options | `Select` | A long radio list | Saves space |
| One of many (more than ~10), or the user knows the name | `Select searchable` | Scrolling a long list | Typing finds it faster |
| A short fixed list in a simple phone-first form, no search | `NativeSelect` | `Select` | The phone's own picker |
| Several from a short list (up to ~6), inside a form | `Checkbox.Group` | `MultiSelect` | All options visible |
| Several filters that change a list at once | `Chip.Group multiple` | `Checkbox.Group` + Apply | Compact, immediate |
| Several from a long list | `MultiSelect searchable` | Dozens of checkboxes | Search plus pills for the picked values |
| A list of free values (tags, email addresses), suggestions optional | `TagsInput` | `MultiSelect` | `MultiSelect` cannot accept new values |
| Free text where suggestions help but any value is valid (city, previous search) | `Autocomplete` | `Select` | `Autocomplete` is a text input; the value is what was typed |
| A fixed set where value and label differ (`{ value: 'vn', label: 'Vietnam' }`) | `Select` | `Autocomplete` | `Autocomplete` has no value/label pairs |
| Rich options, custom layout, async search, a picker opened from a button | `Combobox` | Rebuilding a dropdown | Try `Select` with `renderOption` first |
| Switching between content panels or pages | Tabs, see [navigation](navigation.md) | `SegmentedControl` | Segments are not links or tabs |

## Rules

1. Match the control to the save model ([saving](../patterns/saving.md)): `Switch`, `SegmentedControl` and filter
   chips apply at once; `Checkbox`, `Radio`, `Select`, `MultiSelect`, `TagsInput` inside a form wait for Save.
   Never put a `Switch` in a form with a Save button, and never auto-save a `Checkbox`, `Radio.Group` or `NativeSelect`.
   Keyboard and screen-reader users change a radio or native select just by reading through it with arrow keys.
2. A multi-value choice that changes saved data gets an explicit Save. Do not save when a dropdown closes.
   Filters that only change the current view may apply at once.
3. Preselect the most likely option when a wrong guess is cheap. Leave it empty when the choice matters (a plan, a
   role, a region) or is consent; a consent checkbox is never pre-checked.
4. Give "none" a home. A radio group cannot be cleared: add an explicit option ("No limit", "None"). An optional
   `Select` gets `clearable`. A required `Select` gets `allowDeselect={false}` (Mantine's default is `true`).
   `SegmentedControl` always has a value; do not use it when "no choice" is valid.
5. Label every group: `label` on `Checkbox.Group`, `Radio.Group`, `Switch.Group`, `Select`. `SegmentedControl`
   has no `label` prop: render a visible `Text` label and point to it with `aria-labelledby`.
6. Put required marks and errors on the group, not on each option. A single required checkbox (terms) is the
   exception: its `error` goes on that `Checkbox`.
7. Name a `Switch` after the setting, not its state: "Email notifications", not "Turn on" or "Enabled". Do not use
   a `Switch` for a mixed state; a parent `Checkbox` with `indeterminate` covers "some selected".
8. Keep checkboxes independent. Checking one never checks, unchecks or disables another, except a "Select all"
   parent with `indeterminate`.
9. Order options logically: most likely first for short lists, alphabetical for long ones. Do not reorder options
   while the user is choosing.
10. `SegmentedControl`: 2–5 text segments with short nouns ("List", "Grid"), all the same kind (all text, or all
    icon + text). Icon-only segments need a `VisuallyHidden` label and a `Tooltip`. Too wide on a phone → `Select`.
11. Searchable lists show what happened when nothing matches: set `nothingFoundMessage` ("No servers match").
12. Show a loading state while options load (`Select` and `MultiSelect` accept `loading` in v9; v8: put a `Loader`
    in `rightSection`). See [loading](../patterns/loading.md).
13. On phones, check that the dropdown fits and the selected value is readable; prefer `NativeSelect` for short,
    simple lists in phone-first forms.

## Recipes

### Setting that applies at once

```tsx
import { Switch } from '@mantine/core';

<Switch
  label="Email notifications"
  description="Get an email when a backup fails."
  checked={enabled}
  onChange={(event) => onToggle(event.currentTarget.checked)} // saves now, reverts on error
/>
```

### One of a few, in a form

```tsx
import { Radio, Stack } from '@mantine/core';

<Radio.Group label="Backup frequency" required key={form.key('frequency')} {...form.getInputProps('frequency')}>
  <Stack gap="xs" mt="xs">
    <Radio value="daily" label="Daily" />
    <Radio value="weekly" label="Weekly" />
    <Radio value="never" label="Never" description="You can still back up by hand." />
  </Stack>
</Radio.Group>
```

### View mode with a visible label

```tsx
import { Group, SegmentedControl, Text } from '@mantine/core';

<Group gap="sm">
  <Text id="view-label" size="sm" fw={500}>View</Text>
  <SegmentedControl aria-labelledby="view-label" value={view} onChange={setView} data={['List', 'Grid']} />
</Group>
```

### One of many, fixed values

```tsx
import { Select } from '@mantine/core';

<Select
  label="Region"
  searchable
  allowDeselect={false}
  nothingFoundMessage="No region matches"
  data={[{ value: 'sg', label: 'Singapore' }, { value: 'fra', label: 'Frankfurt' }]}
  key={form.key('region')}
  {...form.getInputProps('region')}
/>
```

### Free values with suggestions

```tsx
import { TagsInput } from '@mantine/core';

<TagsInput label="Notify" description="Press Enter or comma after each address." data={teamEmails} maxTags={10} />
```

## Pitfalls

- A `Switch` inside a settings form with a Save button. Fix: `Checkbox`, or move the switch out and save it at once.
- A `Radio.Group` or `NativeSelect` that saves on change. Fix: put it in a form with Save, or use `SegmentedControl`.
- `Autocomplete` used as a searchable select, so the form receives text that is not a valid value. Fix: `Select searchable`.
- `MultiSelect` for tags the user invents. Fix: `TagsInput`.
- A required `Select` the user can clear by clicking the chosen option again. Fix: `allowDeselect={false}`.
- A `SegmentedControl` with seven segments, or one that wraps on a phone. Fix: `Select`, or fewer segments.
- An error under each checkbox of a group. Fix: one `error` on the `Checkbox.Group`.
- Placeholder "Select…" as the only label. Fix: a visible `label`; see [forms](../patterns/forms.md).

## Checklist

- [ ] Controls that apply at once (`Switch`, `SegmentedControl`, filter chips) are not inside a form with a Save button.
- [ ] No `Checkbox`, `Radio.Group`, `NativeSelect` or multi-select saves data on change or on close.
- [ ] Each group and select has a visible label; a `SegmentedControl` is labelled with `aria-labelledby`.
- [ ] Exclusive choices of 2–6 visible options use `Radio.Group`; longer lists use `Select`, searchable beyond ~10.
- [ ] Free values use `Autocomplete` or `TagsInput`; fixed values use `Select` or `MultiSelect`.
- [ ] "None" is possible where it is valid (explicit option or `clearable`); required selects use `allowDeselect={false}`.
- [ ] Defaults are preselected only where a wrong guess is cheap; consent is never pre-checked.
- [ ] Switch labels name the setting, not the state.
- [ ] Searchable lists set `nothingFoundMessage`.
