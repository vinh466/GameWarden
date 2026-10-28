import 'i18next';
import type { en } from './en';

// Typed keys: t('nav.dashboard') is checked against en.ts.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation';
    resources: { translation: typeof en };
  }
}
