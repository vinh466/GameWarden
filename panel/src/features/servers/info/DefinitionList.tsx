import type { ReactNode } from 'react';
import { IconCheck, IconCopy } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon, CopyButton, Group, Stack, Text, Tooltip } from '@mantine/core';
import classes from './DefinitionList.module.css';

export type DefinitionItem = {
  label: string;
  value: ReactNode;
  /** When set, a copy button copies this exact value. */
  copy?: string;
};

/** Label/value rows that stack on phones and align in two columns from md (docs/22 §7.7). */
export function DefinitionList({ items }: { items: DefinitionItem[] }) {
  return (
    <Stack gap="sm">
      {items.map((item) => (
        <div key={item.label} className={classes.row}>
          <Text size="xs" c="dimmed" className={classes.label}>
            {item.label}
          </Text>
          <Group gap={6} wrap="nowrap" align="flex-start" className={classes.value}>
            <div className={classes.content}>{item.value}</div>
            {item.copy && <CopyValue value={item.copy} />}
          </Group>
        </div>
      ))}
    </Stack>
  );
}

function CopyValue({ value }: { value: string }) {
  const { t } = useTranslation();
  return <CopyIconButton value={value} label={t('actions.copy')} />;
}

/** The same copy affordance outside a definition row (the launch command). */
export function CopyIconButton({ value, label }: { value: string; label: string }) {
  const { t } = useTranslation();
  return (
    <CopyButton value={value} timeout={1500}>
      {({ copied, copy }) => (
        <Tooltip label={copied ? t('actions.copied') : label} withArrow>
          <ActionIcon
            variant="subtle"
            color={copied ? 'green' : 'gray'}
            size="sm"
            aria-label={label}
            onClick={copy}
          >
            {copied ? <IconCheck size={14} /> : <IconCopy size={14} />}
          </ActionIcon>
        </Tooltip>
      )}
    </CopyButton>
  );
}
