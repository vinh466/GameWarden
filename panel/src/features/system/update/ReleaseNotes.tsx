import { ScrollArea, Text } from '@mantine/core';

/** Release notes are GitHub's Markdown body; the panel keeps it readable as text and never renders raw HTML. */
export function ReleaseNotes({ notes, maxHeight = 240 }: { notes: string; maxHeight?: number }) {
  return (
    <ScrollArea.Autosize mah={maxHeight} type="auto">
      <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
        {notes}
      </Text>
    </ScrollArea.Autosize>
  );
}
