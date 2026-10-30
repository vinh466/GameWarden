import { collapse, lineDiff, type DiffLine } from './lineDiff';

const show = (diff: DiffLine[]) =>
  diff.map((line) => ({ same: ' ', added: '+', removed: '-' })[line.kind] + line.text);

/** The old text is the same lines without the added ones, the new one without the removed ones. */
function rebuilds(oldText: string, newText: string) {
  const diff = lineDiff(oldText, newText);
  const lines = (text: string) => (text === '' ? [] : text.split(/\r?\n/));
  expect(diff.filter((line) => line.kind !== 'added').map((line) => line.text)).toEqual(
    lines(oldText)
  );
  expect(diff.filter((line) => line.kind !== 'removed').map((line) => line.text)).toEqual(
    lines(newText)
  );
  return diff;
}

describe('lineDiff', () => {
  it('marks a changed line as removed then added, between unchanged ones', () => {
    expect(
      show(
        lineDiff('[General]\nEnabled = true\nLevel = 3', '[General]\nEnabled = false\nLevel = 3')
      )
    ).toEqual([' [General]', '-Enabled = true', '+Enabled = false', ' Level = 3']);
  });

  it('finds the shortest edit between insertions and deletions', () => {
    const diff = rebuilds('a\nb\nc\na\nb\nb\na', 'c\nb\na\nb\na\nc');
    expect(diff.filter((line) => line.kind !== 'same')).toHaveLength(5); // the classic example: D = 5
  });

  it('treats CRLF and LF alike, and empty texts as no lines', () => {
    expect(show(lineDiff('a\r\nb\r\n', 'a\nb\n'))).toEqual([' a', ' b', ' ']);
    expect(show(lineDiff('', 'a'))).toEqual(['+a']);
    expect(show(lineDiff('a', ''))).toEqual(['-a']);
    expect(lineDiff('', '')).toEqual([]);
  });

  it('rebuilds both texts for random edits', () => {
    let seed = 7;
    const random = () => (seed = (seed * 16_807) % 2_147_483_647) / 2_147_483_647;
    for (let round = 0; round < 50; round++) {
      const base = Array.from({ length: 30 }, () => `line ${Math.floor(random() * 8)}`);
      const changed = base.flatMap((line) =>
        random() < 0.2 ? [] : random() < 0.2 ? [line, `new ${Math.floor(random() * 5)}`] : [line]
      );
      rebuilds(base.join('\n'), changed.join('\n'));
    }
  });

  it('falls back to removed then added when the texts differ too much', () => {
    const oldText = Array.from({ length: 3_000 }, (_, index) => `old ${index}`).join('\n');
    const newText = Array.from({ length: 3_000 }, (_, index) => `new ${index}`).join('\n');
    const diff = rebuilds(oldText, newText);
    expect(diff.slice(0, 3_000).every((line) => line.kind === 'removed')).toBe(true);
  });
});

describe('collapse', () => {
  const same = (count: number, prefix = 's'): DiffLine[] =>
    Array.from({ length: count }, (_, index) => ({ kind: 'same', text: `${prefix}${index}` }));

  it('keeps three lines of context around a change and cuts the rest', () => {
    const diff: DiffLine[] = [...same(10, 'a'), { kind: 'added', text: 'x' }, ...same(10, 'b')];
    expect(collapse(diff)).toEqual([
      { kind: 'skip', count: 7 },
      ...same(10, 'a').slice(7),
      { kind: 'added', text: 'x' },
      ...same(10, 'b').slice(0, 3),
      { kind: 'skip', count: 7 },
    ]);
  });

  it('keeps short runs between changes whole', () => {
    const diff: DiffLine[] = [
      { kind: 'removed', text: 'x' },
      ...same(7),
      { kind: 'added', text: 'y' },
    ];
    expect(collapse(diff)).toEqual(diff);
  });

  it('cuts a long run between two changes in the middle', () => {
    const diff: DiffLine[] = [
      { kind: 'removed', text: 'x' },
      ...same(20),
      { kind: 'added', text: 'y' },
    ];
    expect(collapse(diff).filter((line) => line.kind === 'skip')).toEqual([
      { kind: 'skip', count: 14 },
    ]);
  });
});
