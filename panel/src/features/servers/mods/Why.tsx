/* oxlint-disable jsx-a11y/no-noninteractive-tabindex -- a focusable span lets the tooltip explain a disabled control */
import type { ReactNode } from 'react';
import { Tooltip } from '@mantine/core';

/** A disabled action keeps its place and says why (docs/11 §6 rule 2). */
export function Why({ reason, children }: { reason: string | null; children: ReactNode }) {
  return reason ? (
    <Tooltip label={reason} multiline maw={280}>
      <span tabIndex={0}>{children}</span>
    </Tooltip>
  ) : (
    children
  );
}
