import dayjs from 'dayjs';
import 'dayjs/locale/vi';
import duration from 'dayjs/plugin/duration';
import localizedFormat from 'dayjs/plugin/localizedFormat';
import relativeTime from 'dayjs/plugin/relativeTime';
import i18n from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import { en } from './en';
import { vi } from './vi';

// English and Vietnamese (docs/11 §15 D1): follows the browser, the choice in the account menu is remembered.
export const languages = [
  { code: 'en', label: 'English' },
  { code: 'vi', label: 'Tiếng Việt' },
] as const;

export type LanguageCode = (typeof languages)[number]['code'];

dayjs.extend(relativeTime);
dayjs.extend(duration);
dayjs.extend(localizedFormat);

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: { en: { translation: en }, vi: { translation: vi } },
    fallbackLng: 'en',
    supportedLngs: languages.map((language) => language.code),
    nonExplicitSupportedLngs: true,
    interpolation: { escapeValue: false }, // React escapes
    detection: {
      order: ['localStorage', 'navigator'],
      caches: ['localStorage'],
      lookupLocalStorage: 'gamewarden.language',
    },
  });

const syncDayjs = (language: string) => dayjs.locale(language.startsWith('vi') ? 'vi' : 'en');
syncDayjs(i18n.language);
i18n.on('languageChanged', syncDayjs);

export default i18n;
