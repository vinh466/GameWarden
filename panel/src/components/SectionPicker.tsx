import type { Icon } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Group, Select, Text } from '@mantine/core';

/** A section picker for phones (docs/22 §9.2), where a scrolling tab strip hides half the sections. */
export function SectionPicker({
  value,
  options,
  onChange,
}: {
  value: string;
  options: { value: string; label: string; icon?: Icon }[];
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  const icons = new Map(options.map((option) => [option.value, option.icon]));

  return (
    <Select
      label={t('nav.section')}
      data={options.map(({ value: optionValue, label }) => ({ value: optionValue, label }))}
      value={value}
      onChange={(next) => {
        if (next) {
          onChange(next);
        }
      }}
      renderOption={({ option }) => {
        const Icon = icons.get(option.value);
        return (
          <Group gap="xs" wrap="nowrap">
            {Icon && <Icon size={16} aria-hidden />}
            <Text size="sm">{option.label}</Text>
          </Group>
        );
      }}
      allowDeselect={false}
      mb="lg"
    />
  );
}
