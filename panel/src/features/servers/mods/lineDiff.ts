/** One line of a comparison: in both texts, only in the new one, or only in the old one. */
export interface DiffLine {
  kind: 'same' | 'added' | 'removed';
  text: string;
}

/** A run of unchanged lines left out of the view, between the changes and their context. */
export interface DiffSkip {
  kind: 'skip';
  count: number;
}

/** Above this many changed lines, the middle of the texts is shown as removed, then added (keeps memory bounded). */
const maxEdits = 2_000;

const lines = (text: string) => (text === '' ? [] : text.split(/\r?\n/));

/**
 * A line diff of two texts (Myers' O((N+M)·D) algorithm, as `diff` uses), CRLF and LF compared alike. The common start
 * and end are matched first; between them, more than {@link maxEdits} changes give "all removed, then all added".
 */
export function lineDiff(oldText: string, newText: string): DiffLine[] {
  const a = lines(oldText);
  const b = lines(newText);
  let start = 0;
  while (start < a.length && start < b.length && a[start] === b[start]) {
    start++;
  }

  let endA = a.length;
  let endB = b.length;
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA--;
    endB--;
  }

  const middleA = a.slice(start, endA);
  const middleB = b.slice(start, endB);
  const middle = myers(middleA, middleB) ?? [
    ...middleA.map((text): DiffLine => ({ kind: 'removed', text })),
    ...middleB.map((text): DiffLine => ({ kind: 'added', text })),
  ];
  return [
    ...a.slice(0, start).map((text): DiffLine => ({ kind: 'same', text })),
    ...middle,
    ...a.slice(endA).map((text): DiffLine => ({ kind: 'same', text })),
  ];
}

/** Unchanged runs longer than twice the context are cut down to `context` lines on each side of a change. */
export function collapse(diff: DiffLine[], context = 3): (DiffLine | DiffSkip)[] {
  const result: (DiffLine | DiffSkip)[] = [];
  let index = 0;
  const kind = (at: number) => diff[at]?.kind;
  while (index < diff.length) {
    const line = diff[index];
    if (line && line.kind !== 'same') {
      result.push(line);
      index++;
      continue;
    }

    let end = index;
    while (end < diff.length && kind(end) === 'same') {
      end++;
    }

    const keepBefore = index === 0 ? 0 : context;
    const keepAfter = end === diff.length ? 0 : context;
    if (end - index > keepBefore + keepAfter + 1) {
      result.push(...diff.slice(index, index + keepBefore));
      result.push({ kind: 'skip', count: end - index - keepBefore - keepAfter });
      result.push(...diff.slice(end - keepAfter, end));
    } else {
      result.push(...diff.slice(index, end));
    }

    index = end;
  }

  return result;
}

/** The shortest edit script, or null when it needs more than {@link maxEdits} changes. */
function myers(a: string[], b: string[]): DiffLine[] | null {
  const n = a.length;
  const m = b.length;
  const offset = n + m + 1;
  const v = new Int32Array(2 * offset + 1);
  const get = (k: number) => v[offset + k] ?? 0;
  // trace[d] holds v for k in [-d, d] as it was before step d, for the way back.
  const trace: Int32Array[] = [];
  for (let d = 0; d <= Math.min(n + m, maxEdits); d++) {
    trace.push(v.slice(offset - d, offset + d + 1));
    for (let k = -d; k <= d; k += 2) {
      let x = k === -d || (k !== d && get(k - 1) < get(k + 1)) ? get(k + 1) : get(k - 1) + 1;
      let y = x - k;
      while (x < n && y < m && a[x] === b[y]) {
        x++;
        y++;
      }

      v[offset + k] = x;
      if (x >= n && y >= m) {
        return backtrack(trace, a, b);
      }
    }
  }

  return null;
}

function backtrack(trace: Int32Array[], a: string[], b: string[]): DiffLine[] {
  const result: DiffLine[] = [];
  const line = (texts: string[], index: number) => texts[index] ?? '';
  let x = a.length;
  let y = b.length;
  for (let d = trace.length - 1; d > 0; d--) {
    const v = trace[d] ?? new Int32Array(0);
    const at = (k: number) => v[k + d] ?? 0;
    const k = x - y;
    const previousK = k === -d || (k !== d && at(k - 1) < at(k + 1)) ? k + 1 : k - 1;
    const previousX = at(previousK);
    const previousY = previousX - previousK;
    while (x > previousX && y > previousY) {
      result.push({ kind: 'same', text: line(a, --x) });
      y--;
    }

    if (x === previousX) {
      result.push({ kind: 'added', text: line(b, --y) });
    } else {
      result.push({ kind: 'removed', text: line(a, --x) });
    }
  }

  while (x > 0 && y > 0) {
    result.push({ kind: 'same', text: line(a, --x) });
    y--;
  }

  return result.reverse();
}
