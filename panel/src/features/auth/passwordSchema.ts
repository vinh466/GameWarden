import type { TFunction } from 'i18next';
import { z } from 'zod';

/** docs/06 §4: at least 10 characters, nothing else; typed twice where it is chosen. */
export function newPasswordSchema(t: TFunction) {
  return z
    .object({
      password: z.string().min(10, t('auth.passwordTooShort')).max(256),
      repeat: z.string(),
    })
    .refine((values) => values.password === values.repeat, {
      message: t('auth.passwordsDiffer'),
      path: ['repeat'],
    });
}
