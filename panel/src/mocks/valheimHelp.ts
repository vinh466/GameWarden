import type { AdapterHelp, HelpTopic } from '@/api/generated';
import help from '../../../adapters/valheim/help.json';

// The real Valheim help.json, turned into what Core's GET /adapters/{id}/help answers (absent fields are null), so
// `dev:mock` shows the topics the VM will show (04 §5.3).
interface FileBlock {
  p?: string;
  list?: string[];
  tip?: string;
  warning?: string;
}

interface FileTopic {
  title?: string;
  summary: string;
  body: FileBlock[];
  values?: { value: string; text: string }[];
  tested?: string;
  verified: boolean;
  sources: { title: string; url: string; publisher?: string; checked: string }[];
  related?: string[];
}

const { topics } = help as { topics: Record<string, FileTopic> };

const topic = (file: FileTopic): HelpTopic => ({
  title: file.title ?? null,
  summary: file.summary,
  body: file.body.map((block) => ({
    p: block.p ?? null,
    list: block.list ?? null,
    tip: block.tip ?? null,
    warning: block.warning ?? null,
  })),
  values: file.values ?? null,
  tested: file.tested ?? null,
  verified: file.verified,
  sources: file.sources.map((source) => ({ ...source, publisher: source.publisher ?? null })),
  related: file.related ?? null,
});

export const valheimHelp: AdapterHelp = {
  topics: Object.fromEntries(Object.entries(topics).map(([key, file]) => [key, topic(file)])),
};

/** Whether help.json has a topic for a setting key or `group:<id>`, like Core's `hasHelp`. */
export const hasValheimHelp = (key: string) => key in topics;
