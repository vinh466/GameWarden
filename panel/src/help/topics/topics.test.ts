import dayjs from 'dayjs';
import type { HelpTopic } from '@/components/help/types';
import { isPanelTopicId, panelTopics } from '.';

// docs/12 §5.1: every panel topic has a summary, a body, at least one source and a date per source.
const topics: [string, HelpTopic][] = Object.entries(panelTopics);
const today = dayjs().format('YYYY-MM-DD'); // local: a source checked today is not in the future

describe('panel help topics', () => {
  it('include the topics of docs/12 §6.2', () => {
    expect(Object.keys(panelTopics).sort()).toEqual(
      [
        'auth.password',
        'setup.token',
        'system.channel',
        'system.panelNetworks',
        'system.restoreDb',
        'system.vpnRanges',
        'wizard.id',
        'wizard.name',
        'wizard.portsAuto',
      ].sort()
    );
  });

  it.each(topics)('%s is complete', (_id, topic) => {
    // A panel topic names itself, so a related link to it has a title.
    expect(topic.title?.trim()).toBeTruthy();
    expect(topic.summary.trim()).not.toBe('');
    expect(topic.body.length).toBeGreaterThan(0);
    expect(topic.sources.length).toBeGreaterThan(0);
    for (const value of topic.values ?? []) {
      expect(value.value.trim()).not.toBe('');
      expect(value.text.trim()).not.toBe('');
    }
  });

  it.each(topics)('%s cites https sources with a checked date', (_id, topic) => {
    for (const source of topic.sources) {
      expect(source.title.trim()).not.toBe('');
      expect(new URL(source.url).protocol).toBe('https:');
      expect(source.checked).toMatch(/^\d{4}-\d{2}-\d{2}$/);
      // A real calendar date, not later than today.
      expect(new Date(`${source.checked}T00:00:00Z`).toISOString().slice(0, 10)).toBe(
        source.checked
      );
      expect(source.checked <= today).toBe(true);
    }
  });

  it.each(topics)('%s relates only to topics that exist', (id, topic) => {
    for (const related of topic.related ?? []) {
      expect(isPanelTopicId(related)).toBe(true);
      expect(related).not.toBe(id);
    }
  });
});
