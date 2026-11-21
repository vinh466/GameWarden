import { useTranslation } from 'react-i18next';
import { Anchor, Badge, Card, Checkbox, Group, Stack, Text } from '@mantine/core';
import type { ModEntryRequest, ModEntryView, ModPackageState } from '@/api/generated';
import { displayName, packageUrl, withEnabled, withMod, withoutMod } from './modEntries';
import { ModRowMenu } from './ModRowMenu';
import { ModStateBadge } from './ModStateBadge';

/**
 * The mod set as cards on phones (docs/22 §7.8): name, package, version, newer, on/off, Thunderstore state and the
 * row menu, with the same selection as the table.
 */
export function ModsMobileList({
  entries,
  newer,
  states,
  busy,
  busyReason,
  community,
  checked,
  onCheckedChange,
  chosen,
  onChange,
}: {
  entries: ModEntryView[];
  newer: Map<string, string>;
  states: Map<string, ModPackageState>;
  busy: boolean;
  busyReason: string | null;
  community: string | null;
  checked: ModEntryView[];
  onCheckedChange: (entries: ModEntryView[]) => void;
  chosen: ModEntryRequest[];
  onChange: (entries: ModEntryRequest[]) => void;
}) {
  const { t } = useTranslation();

  return (
    <Stack gap="xs" p="xs">
      {entries.map((entry) => {
        const dependency = entry.origin === 'dependency';
        const latest = newer.get(entry.package);
        const selected = checked.some((item) => item.package === entry.package);
        return (
          <Card key={entry.package} withBorder p="sm">
            <Group align="flex-start" gap="xs" wrap="nowrap">
              <Checkbox
                mt={2}
                aria-label={t('mods.select', { mod: entry.package })}
                checked={selected}
                disabled={dependency || busy}
                onChange={(event) =>
                  onCheckedChange(
                    event.currentTarget.checked
                      ? [...checked, entry]
                      : checked.filter((item) => item.package !== entry.package)
                  )
                }
              />
              <Stack gap={4} miw={0} style={{ flex: 1 }}>
                <Group gap={6} wrap="wrap">
                  <Anchor
                    href={packageUrl(community, entry.package)}
                    target="_blank"
                    size="sm"
                    fw={500}
                  >
                    {displayName(entry.package)}
                  </Anchor>
                  {dependency && (
                    <Badge size="xs" variant="outline" color="gray">
                      {t('mods.dependency')}
                    </Badge>
                  )}
                </Group>
                <Text size="xs" c="dimmed" ff="monospace" truncate title={entry.package}>
                  {entry.package}
                </Text>
                <Group gap="xs" wrap="wrap">
                  <Text size="xs" ff="monospace">
                    {entry.version}
                  </Text>
                  {latest && (
                    <Badge size="xs" variant="light" color="violet" ff="monospace">
                      {latest}
                    </Badge>
                  )}
                  <Badge size="xs" variant="light" color={entry.enabled ? 'green' : 'gray'}>
                    {entry.enabled ? t('mods.enabledYes') : t('mods.enabledNo')}
                  </Badge>
                  <ModStateBadge state={states.get(entry.package)} />
                </Group>
              </Stack>
              <ModRowMenu
                entry={entry}
                dependency={dependency}
                busy={busy}
                busyReason={busyReason}
                latest={latest}
                onToggleEnabled={() => onChange(withEnabled(chosen, entry.package, !entry.enabled))}
                onUpdate={(version) =>
                  onChange(withMod(chosen, { package: entry.package, version }))
                }
                onRemove={() => onChange(withoutMod(chosen, entry.package))}
              />
            </Group>
          </Card>
        );
      })}
    </Stack>
  );
}
