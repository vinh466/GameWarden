import dayjs from 'dayjs';
import { useState } from 'react';
import { IconSearch } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import {
  Alert,
  Button,
  Divider,
  Group,
  Loader,
  Modal,
  Stack,
  Text,
  TextInput,
} from '@mantine/core';
import { schemaResolver, useForm } from '@mantine/form';
import { useDebouncedValue, useMediaQuery } from '@mantine/hooks';
import type { ModEntryRequest, ModsView } from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { useHelpHost } from '@/components/help/useHelp';
import { chosenEntries, parsePackage, withMod } from './modEntries';
import { SearchHitRow } from './SearchHitRow';
import { useModsSync } from './useModsSync';

/**
 * Add a mod (docs/13 §4.4 items 2 and 3): search Thunderstore's list as one types (debounced), add a result at its
 * latest version; or name it exactly, `Namespace-Name[@version]`, which works without the list. Core adds what it needs.
 */
export function AddModModal({
  id,
  server,
  mods,
  opened,
  onClose,
}: {
  id: string;
  server: string;
  mods: ModsView;
  opened: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const helpHost = useHelpHost();
  const phone = useMediaQuery('(max-width: 36em)');
  const [text, setText] = useState('');
  const [query] = useDebouncedValue(text.trim(), 400);
  const search = useQuery({
    ...queries.modSearch(id, query),
    enabled: opened && query.length >= 2,
  });
  const sync = useModsSync(id, server);
  const chosen = chosenEntries(mods.active);
  const form = useForm({
    mode: 'uncontrolled',
    initialValues: { exact: '' },
    validate: schemaResolver(
      z.object({
        exact: z
          .string()
          .refine((value) => parsePackage(value) !== null, t('mods.add.exactInvalid')),
      }),
      { sync: true }
    ),
  });
  const close = () => {
    setText('');
    form.reset();
    onClose();
  };
  const add = (entry: ModEntryRequest) =>
    sync.mutate({ entries: withMod(chosen, { ...entry, enabled: true }) }, { onSuccess: close });

  return (
    <Modal
      opened={opened}
      onClose={close}
      size="xl"
      fullScreen={phone}
      title={t('mods.add.title')}
      {...helpHost}
    >
      <Stack gap="md">
        <TextInput
          label={t('mods.add.search')}
          placeholder={t('mods.add.searchPlaceholder')}
          description={t('mods.add.searchHint', { community: mods.community ?? 'Thunderstore' })}
          leftSection={<IconSearch size={16} aria-hidden />}
          rightSection={search.isFetching ? <Loader size="xs" /> : null}
          value={text}
          onChange={(event) => setText(event.currentTarget.value)}
          data-autofocus
        />
        {query.length < 2 ? (
          <Text size="sm" c="dimmed">
            {t('mods.add.typeMore')}
          </Text>
        ) : search.isError ? (
          <Alert color="yellow" title={toProblem(search.error, t('errors.generic')).title}>
            {t('mods.add.searchFailed')}
          </Alert>
        ) : search.data ? (
          <Stack gap={0}>
            {search.data.results.length === 0 && (
              <Text size="sm" c="dimmed">
                {t('mods.add.noResults', { q: query })}
              </Text>
            )}
            {search.data.results.map((hit) => (
              <SearchHitRow
                key={hit.package}
                hit={hit}
                inSet={chosen.some((entry) => entry.package === hit.package)}
                adding={
                  sync.isPending &&
                  sync.variables.entries?.some((e) => e.package === hit.package) === true
                }
                disabled={sync.isPending}
                onAdd={() => add({ package: hit.package, version: hit.version })}
              />
            ))}
            <Text size="xs" c="dimmed" mt="xs">
              {t('mods.add.listedAt', { time: dayjs(search.data.fetchedAt).fromNow() })}
            </Text>
          </Stack>
        ) : null}
        <Divider label={t('mods.add.exact')} labelPosition="left" />
        <form
          onSubmit={form.onSubmit((values) => {
            const parsed = parsePackage(values.exact);
            if (parsed) {
              add({ package: parsed.package, version: parsed.version });
            }
          })}
        >
          <Group align="flex-start" gap="xs" wrap="nowrap">
            <TextInput
              flex={1}
              aria-label={t('mods.add.exact')}
              description={t('mods.add.exactHint')}
              placeholder="RandyKnapp-EpicLoot@0.15.4"
              ff="monospace"
              key={form.key('exact')}
              {...form.getInputProps('exact')}
            />
            <Button type="submit" variant="default" loading={sync.isPending}>
              {t('mods.add.addExact')}
            </Button>
          </Group>
        </form>
      </Stack>
    </Modal>
  );
}
