import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button, Group, PasswordInput, Stack } from '@mantine/core';
import { schemaResolver, useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { changePassword } from '@/api/generated';
import { sessionKey } from '@/api/session';
import { HelpField } from '@/components/help/HelpLabel';
import { ProblemAlert } from '@/components/ProblemAlert';
import { newPasswordSchema } from './passwordSchema';

/** Shown in a modal from the account menu. Core ends every other session (docs/06 §3); this one continues. */
export function ChangePasswordForm({ onDone }: { onDone: () => void }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const form = useForm({
    mode: 'uncontrolled',
    initialValues: { current: '', password: '', repeat: '' },
    validate: schemaResolver(
      newPasswordSchema(t).and(z.object({ current: z.string().min(1, t('auth.required')) })),
      { sync: true }
    ),
  });
  const change = useMutation({
    mutationFn: async (values: { current: string; password: string }) =>
      (
        await changePassword({
          body: { currentPassword: values.current, newPassword: values.password },
          throwOnError: true,
        })
      ).data,
    onSuccess: (session) => {
      queryClient.setQueryData(sessionKey(), session); // a new CSRF token comes with the new session
      notifications.show({ color: 'green', message: t('auth.passwordChanged') });
      onDone();
    },
  });

  return (
    <form onSubmit={form.onSubmit((values) => change.mutate(values))}>
      <Stack gap="md">
        {change.isError && <ProblemAlert error={change.error} />}
        <PasswordInput
          label={t('auth.currentPassword')}
          autoComplete="current-password"
          data-autofocus
          withAsterisk
          key={form.key('current')}
          {...form.getInputProps('current')}
        />
        <HelpField label={t('auth.newPassword')} help={{ topicId: 'auth.password' }} required>
          {(id) => (
            <PasswordInput
              id={id}
              description={t('auth.passwordTooShort')}
              autoComplete="new-password"
              key={form.key('password')}
              {...form.getInputProps('password')}
            />
          )}
        </HelpField>
        <PasswordInput
          label={t('auth.repeatPassword')}
          autoComplete="new-password"
          withAsterisk
          key={form.key('repeat')}
          {...form.getInputProps('repeat')}
        />
        <Group justify="flex-end" mt="sm">
          <Button variant="default" onClick={onDone}>
            {t('actions.cancel')}
          </Button>
          <Button type="submit" loading={change.isPending}>
            {t('auth.changePassword')}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
