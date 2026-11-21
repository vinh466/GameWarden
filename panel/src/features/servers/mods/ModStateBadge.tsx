import { useTranslation } from 'react-i18next';
import { Badge, Text, Tooltip } from '@mantine/core';
import type { ModPackageState } from '@/api/generated';

const stateColors: Record<ModPackageState, string> = {
  ok: 'gray',
  deprecated: 'yellow',
  notFound: 'red',
};

/** Thunderstore's state of one package: a badge, or "unknown" with the reason when no check has run. */
export function ModStateBadge({ state }: { state: ModPackageState | undefined }) {
  const { t } = useTranslation();
  if (!state) {
    return (
      <Tooltip label={t('mods.stateUnknownHint')}>
        <Text size="xs" c="dimmed" span>
          {t('mods.state.unknown')}
        </Text>
      </Tooltip>
    );
  }

  return (
    <Badge variant="light" color={stateColors[state]}>
      {t(`mods.state.${state}`)}
    </Badge>
  );
}
