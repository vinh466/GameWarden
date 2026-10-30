import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Group, Stack, Text } from '@mantine/core';
import { collapse, lineDiff } from './lineDiff';
import classes from './DiffView.module.css';

const signs = { same: ' ', added: '+', removed: '−' } as const;

/**
 * Two texts compared line by line (ADR-0021): lines only in `newText` in green, only in `oldText` in red, long unchanged
 * runs collapsed. `legend` says which text is which.
 */
export function DiffView({
  oldText,
  newText,
  legend,
}: {
  oldText: string;
  newText: string;
  legend: string;
}) {
  const { t } = useTranslation();
  const diff = useMemo(() => lineDiff(oldText, newText), [oldText, newText]);
  const added = diff.filter((line) => line.kind === 'added').length;
  const removed = diff.filter((line) => line.kind === 'removed').length;
  if (added === 0 && removed === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t('mods.configs.diff.same')}
      </Text>
    );
  }

  return (
    <Stack gap={6}>
      <Group justify="space-between" gap="xs">
        <Text size="xs" c="dimmed">
          {legend}
        </Text>
        <Text size="xs" fw={600}>
          {t('mods.configs.diff.summary', { added, removed })}
        </Text>
      </Group>
      <div className={classes.diff} role="table" aria-label={legend}>
        {collapse(diff).map((line, index) =>
          line.kind === 'skip' ? (
            <div key={index} className={classes.skip} role="row">
              {t('mods.configs.diff.skipped', { count: line.count })}
            </div>
          ) : (
            <div
              key={index}
              role="row"
              data-kind={line.kind}
              className={`${classes.line} ${line.kind === 'same' ? '' : classes[line.kind]}`}
            >
              <span className={classes.sign} aria-hidden>
                {signs[line.kind]}
              </span>
              <span>{line.text || ' '}</span>
            </div>
          )
        )}
      </div>
    </Stack>
  );
}
