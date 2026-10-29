import type { Source } from '@/components/help/types';

/** A document of this repository as a source (docs/12 §5): its GitHub link on `main`. */
export function repoDoc(path: string, title: string, checked: string): Source {
  return {
    title,
    url: `https://github.com/vinh466/GameWarden/blob/main/${path}`,
    publisher: 'GameWarden',
    checked,
  };
}
