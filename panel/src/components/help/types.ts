import type { PanelTopicId } from '@/help/topics';

/**
 * One explanation in the help drawer (docs/12 §5). The same shape serves the panel's own topics and, later, the
 * adapters' `help.json`. All text is English and plain: no markdown, no HTML (docs/11 §10.7).
 */
export type HelpTopic = {
  /** Default: the label of the field or choice the drawer was opened from. */
  title?: string;
  /** One or two sentences. */
  summary: string;
  body: Block[];
  /** For enum settings and choices; the default is marked from the request's `defaultValue`. */
  values?: { value: string; text: string }[];
  /** What a VM walk-through or a test showed, with its date. */
  tested?: string;
  /** False while any statement rests on an unverified source: the drawer shows "Not verified yet". */
  verified: boolean;
  /** At least one; shown last. */
  sources: Source[];
  /** Topic ids opened in the same drawer. */
  related?: string[];
};

export type Block = { p: string } | { list: string[] } | { tip: string } | { warning: string };

export type Source = {
  title: string;
  /** https only. */
  url: string;
  publisher?: string;
  /** The date the source was last checked, YYYY-MM-DD. */
  checked: string;
};

/**
 * What a help button asks the drawer to show: a panel topic by its id, a topic passed whole, or an adapter's topic by
 * its `help.json` key (a setting key, `group:<id>` or `option:<key>=<value>`), loaded on demand. `useHelpTopic` is the
 * one place that turns each into a topic.
 */
export type HelpRequest = {
  /** The drawer's title, normally the field's label in the panel's language; else the topic's title. */
  title?: string;
  /** The value marked as the default in the values table. */
  defaultValue?: string;
} & ({ topicId: PanelTopicId } | { topic: HelpTopic } | { adapter: { id: string; key: string } });
