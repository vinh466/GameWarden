import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { queries } from '@/api/queries';
import { isPanelTopicId, panelTopics } from '@/help/topics';
import { describeKey, fromApiTopic } from './adapterHelp';
import type { HelpRequest, HelpTopic } from './types';

export interface RelatedTopic {
  title: string;
  request: HelpRequest;
}

export interface HelpTopicState {
  topic?: HelpTopic;
  /** The drawer's title when the request has none. */
  title?: string;
  /** Labels of the values table's values, such as an enum's option labels. */
  valueLabels?: Record<string, string>;
  related: RelatedTopic[];
  isLoading: boolean;
  /** Loading the adapter's topics failed; `retry` loads them again. */
  error: unknown;
  retry: () => void;
}

/**
 * The one place a help request becomes a topic (docs/12 §5.1): a panel topic from the bundled registry, a topic passed
 * whole, or an adapter's topic from `GET /adapters/{id}/help`, fetched once per adapter with the generated client.
 * A key the adapter has no topic for gives no topic, and the drawer says so.
 */
export function useHelpTopic(request: HelpRequest | undefined): HelpTopicState {
  const { t, i18n } = useTranslation();
  const adapterId = request && 'adapter' in request ? request.adapter.id : undefined;
  const help = useQuery({ ...queries.adapterHelp(adapterId ?? ''), enabled: !!adapterId });
  // The labels of settings and groups; the forms with help buttons have loaded them already.
  const adapters = useQuery({ ...queries.adapters(), enabled: !!adapterId });

  const none = { related: [], isLoading: false, error: null, retry: () => undefined };
  if (!request) {
    return none;
  }

  if ('topic' in request) {
    return { ...none, topic: request.topic };
  }

  if ('topicId' in request) {
    const topic: HelpTopic = panelTopics[request.topicId];
    return {
      ...none,
      topic,
      title: topic.title,
      related: (topic.related ?? []).filter(isPanelTopicId).map((id) => ({
        title: (panelTopics[id] as HelpTopic).title ?? id,
        request: { topicId: id },
      })),
    };
  }

  const { id, key } = request.adapter;
  const adapter = adapters.data?.adapters.find((candidate) => candidate.id === id);
  const language = i18n.resolvedLanguage;
  const found = help.data?.topics[key];
  const topic = found ? fromApiTopic(found) : undefined;
  const own = describeKey(adapter, key, language, t);
  return {
    topic,
    title: topic?.title ?? own.title,
    valueLabels: own.valueLabels,
    related: (topic?.related ?? [])
      .filter((relatedKey) => help.data?.topics[relatedKey])
      .map((relatedKey) => {
        const target = describeKey(adapter, relatedKey, language, t);
        const title = help.data?.topics[relatedKey]?.title ?? target.title ?? relatedKey;
        return {
          title,
          request: { title, defaultValue: target.defaultValue, adapter: { id, key: relatedKey } },
        };
      }),
    isLoading: help.isPending,
    error: help.error,
    retry: () => void help.refetch(),
  };
}
