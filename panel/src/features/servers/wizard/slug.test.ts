import { slug } from './slug';

describe('slug', () => {
  it('turns a display name into an instance id', () => {
    expect(slug('Vikings Season 2')).toBe('vikings-season-2');
  });

  it('drops Vietnamese accents', () => {
    expect(slug('Đảo Rồng — mùa 1')).toBe('dao-rong-mua-1');
  });

  it('keeps at most 63 characters and no leading or trailing dash', () => {
    expect(slug(`  ${'a'.repeat(80)}!`)).toBe('a'.repeat(63));
  });
});
