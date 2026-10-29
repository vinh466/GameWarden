import type { TFunction } from 'i18next';
import type { AdapterSummary, HelpBlock, HelpTopic as ApiHelpTopic } from '@/api/generated';
import { groupLabel, localized, settingLabel } from '@/features/servers/settings/settingText';
import type { Block, HelpTopic } from './types';

// Keys of an adapter's help.json (docs/04 §5.3): a setting key, `group:<id>`, or `option:<key>=<value>`.
const groupPrefix = 'group:';
const optionPrefix = 'option:';

/** Core's topic (absent fields are null, a block has all four kinds) as the panel's topic (docs/12 §5). */
export function fromApiTopic(topic: ApiHelpTopic): HelpTopic {
  return {
    title: topic.title ?? undefined,
    summary: topic.summary,
    body: topic.body.flatMap(toBlock),
    values: topic.values ?? undefined,
    tested: topic.tested ?? undefined,
    verified: topic.verified,
    sources: topic.sources.map((source) => ({
      ...source,
      publisher: source.publisher ?? undefined,
    })),
    related: topic.related ?? undefined,
  };
}

function toBlock(block: HelpBlock): Block[] {
  if (block.p != null) {
    return [{ p: block.p }];
  }

  if (block.list != null) {
    return [{ list: block.list }];
  }

  if (block.tip != null) {
    return [{ tip: block.tip }];
  }

  return block.warning != null ? [{ warning: block.warning }] : [];
}

/** What a help key is about, in the panel's language: the label of its setting, group or option. */
export function describeKey(
  adapter: AdapterSummary | undefined,
  key: string,
  language: string | undefined,
  t: TFunction
): { title?: string; defaultValue?: string; valueLabels?: Record<string, string> } {
  if (!adapter) {
    return {};
  }

  if (key.startsWith(groupPrefix)) {
    const group = adapter.settingGroups.find(
      (candidate) => `${groupPrefix}${candidate.id}` === key
    );
    return group ? { title: groupLabel(group, language, t) } : {};
  }

  if (key.startsWith(optionPrefix)) {
    const [settingKey = '', value = ''] = key.slice(optionPrefix.length).split('=');
    const setting = adapter.settings.find((candidate) => candidate.key === settingKey);
    const option = setting?.options.find((candidate) => candidate.value === value);
    return setting && option
      ? {
          title: `${settingLabel(setting, language)}: ${localized(option.label, language) ?? value}`,
        }
      : {};
  }

  const setting = adapter.settings.find((candidate) => candidate.key === key);
  if (!setting) {
    return {};
  }

  const valueLabels = Object.fromEntries(
    setting.options.flatMap((option) => {
      const label = localized(option.label, language);
      return label ? [[option.value, label]] : [];
    })
  );
  return {
    title: settingLabel(setting, language),
    defaultValue: setting.default ?? undefined,
    valueLabels: Object.keys(valueLabels).length > 0 ? valueLabels : undefined,
  };
}
