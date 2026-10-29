import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button, PasswordInput, Stack, TextInput } from '@mantine/core';
import { schemaResolver, useForm } from '@mantine/form';
import { createOwner } from '@/api/generated';
import { sessionKey, setupStatusQuery } from '@/api/session';
import { HelpField } from '@/components/help/HelpLabel';
import { ProblemAlert } from '@/components/ProblemAlert';
import { newPasswordSchema } from '@/features/auth/passwordSchema';

/** Setup step 1 (docs/06 §4): the Owner account, with the one-time token from the setup link; signs the Owner in. */
export function OwnerStep({ token, onCreated }: { token: string; onCreated: () => void }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const form = useForm({
    mode: 'uncontrolled',
    initialValues: { userName: 'owner', password: '', repeat: '' },
    validate: schemaResolver(
      newPasswordSchema(t).and(
        z.object({
          userName: z
            .string()
            .trim()
            .regex(/^[\p{L}\p{N}._-]{1,64}$/u, t('auth.userNameRule')),
        })
      ),
      { sync: true }
    ),
  });
  const create = useMutation({
    mutationFn: async (values: { userName: string; password: string }) =>
      (await createOwner({ body: { setupToken: token, ...values }, throwOnError: true })).data,
    onSuccess: async (session) => {
      queryClient.setQueryData(sessionKey(), session);
      await queryClient.invalidateQueries({ queryKey: setupStatusQuery().queryKey });
      onCreated();
    },
  });

  return (
    <form
      onSubmit={form.onSubmit(({ userName, password }) =>
        create.mutate({ userName: userName.trim(), password })
      )}
    >
      <Stack gap="md">
        {create.isError && <ProblemAlert error={create.error} />}
        <TextInput
          label={t('auth.userName')}
          autoComplete="username"
          withAsterisk
          key={form.key('userName')}
          {...form.getInputProps('userName')}
        />
        <HelpField label={t('auth.password')} help={{ topicId: 'auth.password' }} required>
          {(id) => (
            <PasswordInput
              id={id}
              description={t('auth.passwordTooShort')}
              autoComplete="new-password"
              data-autofocus
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
        <Button type="submit" fullWidth mt="sm" loading={create.isPending}>
          {t('auth.createOwner')}
        </Button>
      </Stack>
    </form>
  );
}
