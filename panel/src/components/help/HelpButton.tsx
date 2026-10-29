import { IconHelpCircle } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { ActionIcon } from '@mantine/core';
import type { HelpRequest } from './types';
import { useHelp } from './useHelp';
import classes from './HelpButton.module.css';

/**
 * The small, quiet trigger of the help drawer (docs/12 §3): placed right after a label, outside the `<label>`
 * element, named "About ‹label›". It appears only where a topic exists.
 */
export function HelpButton({ label, help }: { label: string; help: HelpRequest }) {
  const { t } = useTranslation();
  const { open } = useHelp();
  return (
    <ActionIcon
      variant="subtle"
      color="gray"
      size="sm"
      className={classes.trigger}
      aria-label={t('help.about', { label })}
      onClick={() => open({ title: label, ...help })}
    >
      <IconHelpCircle size={16} stroke={1.75} aria-hidden />
    </ActionIcon>
  );
}
