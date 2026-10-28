import { en } from './en';
import { vi as vietnamese } from './vi';

function keys(value: object, prefix = ''): string[] {
  return Object.entries(value).flatMap(([key, child]) =>
    typeof child === 'string' ? [`${prefix}${key}`] : keys(child as object, `${prefix}${key}.`)
  );
}

describe('translations', () => {
  it('Vietnamese has exactly the English keys (docs/11 §15 D1)', () => {
    expect(keys(vietnamese).sort()).toEqual(keys(en).sort());
  });

  it('keeps every interpolation placeholder', () => {
    // Key → its sorted placeholders, so a failure's diff names the key.
    const placeholders = (dictionary: object) =>
      Object.fromEntries(
        keys(dictionary).map((key) => {
          const text = key
            .split('.')
            .reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], dictionary);
          return [key, (String(text).match(/\{\{\w+\}\}/g) ?? []).sort()];
        })
      );
    expect(placeholders(vietnamese)).toEqual(placeholders(en));
  });
});
