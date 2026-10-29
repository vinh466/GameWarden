import { panelTopics } from '@/help/topics';
import type { HelpRequest, HelpTopic } from './types';

export interface HelpTopicState {
  topic?: HelpTopic;
  /** True while a topic loads; adapter topics will load on demand (docs/12 §5.1). */
  isLoading: boolean;
}

/**
 * The one place a help request becomes a topic. Today: a panel topic from the bundled registry, or a topic passed
 * whole. An adapter's topic (`GET /adapters/{id}/help`) will be read here with TanStack Query and the generated
 * client; a hook, so that variant can use `useQuery` without changing its callers.
 */
export function useHelpTopic(request: HelpRequest | undefined): HelpTopicState {
  if (!request) {
    return { isLoading: false };
  }

  if ('topic' in request) {
    return { topic: request.topic, isLoading: false };
  }

  return { topic: panelTopics[request.topicId], isLoading: false };
}
