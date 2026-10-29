import type { HelpTopic } from '@/components/help/types';
import { authTopics } from './auth';
import { setupTopics } from './setup';
import { systemTopics } from './system';
import { wizardTopics } from './wizard';

/** The panel's own help topics (docs/12 §5.1, §6.2), bundled with the panel; one file per area. */
export const panelTopics = {
  ...systemTopics,
  ...wizardTopics,
  ...authTopics,
  ...setupTopics,
} satisfies Record<string, HelpTopic>;

export type PanelTopicId = keyof typeof panelTopics;

export function isPanelTopicId(id: string): id is PanelTopicId {
  return Object.hasOwn(panelTopics, id);
}
