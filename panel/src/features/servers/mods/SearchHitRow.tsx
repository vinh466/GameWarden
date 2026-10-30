import { useTranslation } from 'react-i18next';
import { Anchor, Badge, Button, Group, Stack, Text } from '@mantine/core';
import type { ModSearchHit } from '@/api/generated';
import classes from './SearchHitRow.module.css';

/** One Thunderstore search result: name, author, latest version, description, downloads, and *Add*. */
export function SearchHitRow({
  hit,
  inSet,
  adding,
  disabled,
  onAdd,
}: {
  hit: ModSearchHit;
  inSet: boolean;
  adding: boolean;
  disabled: boolean;
  onAdd: () => void;
}) {
  const { t, i18n } = useTranslation();
  const downloads = new Intl.NumberFormat(i18n.language, { notation: 'compact' }).format(
    hit.downloads
  );
  return (
    <Group
      className={classes.row}
      justify="space-between"
      wrap="nowrap"
      align="flex-start"
      gap="sm"
      py="xs"
    >
      <Stack gap={2} miw={0}>
        <Group gap={6} wrap="wrap">
          <Anchor href={hit.url} target="_blank" fw={600} size="sm">
            {hit.name.replaceAll('_', ' ')}
          </Anchor>
          <Text size="xs" c="dimmed">
            {hit.owner}
          </Text>
          <Badge size="xs" variant="light" color="gray" ff="monospace">
            {hit.version}
          </Badge>
          {hit.deprecated && (
            <Badge size="xs" variant="light" color="yellow">
              {t('mods.state.deprecated')}
            </Badge>
          )}
        </Group>
        <Text size="sm" c="dimmed" lineClamp={2}>
          {hit.description}
        </Text>
        <Text size="xs" c="dimmed">
          {t('mods.add.downloads', { value: downloads })}
        </Text>
      </Stack>
      {inSet ? (
        <Badge variant="outline" color="gray" className={classes.add}>
          {t('mods.add.inSet')}
        </Badge>
      ) : (
        <Button
          size="xs"
          variant="light"
          loading={adding}
          disabled={disabled}
          onClick={onAdd}
          aria-label={`${t('mods.add.addVersion', { version: hit.version })}: ${hit.package}`}
          className={classes.add}
        >
          {t('mods.add.addVersion', { version: hit.version })}
        </Button>
      )}
    </Group>
  );
}
