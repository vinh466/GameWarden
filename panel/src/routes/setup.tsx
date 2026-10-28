import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Alert, Button, Code, PasswordInput, Stack, TextInput } from '@mantine/core';
import { schemaResolver, useForm } from '@mantine/form';
import { createOwner } from '@/api/generated';
import { sessionKey, setupStatusQuery } from '@/api/session';
import { ProblemAlert } from '@/components/ProblemAlert';
import { AuthCard } from '@/features/auth/AuthCard';
import { newPasswordSchema } from '@/features/auth/passwordSchema';

// First-run setup (docs/06 §4): the installer and `gamewarden setup-url` open /setup?token=… on the machine itself.
export const Route = createFileRoute('/setup')({
  validateSearch: z.object({ token: z.string().optional() }),
  beforeLoad: async ({ context: { queryClient } }) => {
    const status = await queryClient.query(setupStatusQuery()).catch(() => null);
    if (status && !status.needsSetup) {
      throw redirect({ to: '/login' });
    }
  },
  component: Setup,
});

function Setup() {
  const { t } = useTranslation();
  const { token } = Route.useSearch();
  const router = useRouter();
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
      (await createOwner({ body: { setupToken: token ?? '', ...values }, throwOnError: true }))
        .data,
    onSuccess: async (session) => {
      queryClient.setQueryData(sessionKey(), session);
      await queryClient.invalidateQueries({ queryKey: setupStatusQuery().queryKey });
      router.history.push('/');
    },
  });

  return (
    <AuthCard title={t('auth.setupTitle')} text={t('auth.setupText')}>
      {!token ? (
        <Alert color="yellow">
          {t('auth.setupNoToken')}
          <Code block mt="xs">
            gamewarden setup-url
          </Code>
        </Alert>
      ) : (
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
            <PasswordInput
              label={t('auth.password')}
              description={t('auth.passwordTooShort')}
              autoComplete="new-password"
              data-autofocus
              withAsterisk
              key={form.key('password')}
              {...form.getInputProps('password')}
            />
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
      )}
    </AuthCard>
  );
}
