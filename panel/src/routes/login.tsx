import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button, Code, PasswordInput, Stack, Text, TextInput } from '@mantine/core';
import { schemaResolver, useForm } from '@mantine/form';
import { login } from '@/api/generated';
import { safeRedirect, sessionKey, sessionQuery, setupStatusQuery } from '@/api/session';
import { ProblemAlert } from '@/components/ProblemAlert';
import { AuthCard } from '@/features/auth/AuthCard';

// TanStack Router's "Authenticated Routes" guide: the guarded layout sends here with ?redirect=<where the user was>.
export const Route = createFileRoute('/login')({
  validateSearch: z.object({ redirect: z.string().optional() }),
  beforeLoad: async ({ context: { queryClient } }) => {
    if (await queryClient.query(sessionQuery()).catch(() => null)) {
      throw redirect({ to: '/' });
    }

    if ((await queryClient.query(setupStatusQuery()).catch(() => null))?.needsSetup) {
      throw redirect({ to: '/setup' });
    }
  },
  component: Login,
});

function Login() {
  const { t } = useTranslation();
  const search = Route.useSearch();
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm({
    mode: 'uncontrolled',
    initialValues: { userName: '', password: '' },
    validate: schemaResolver(
      z.object({
        userName: z.string().trim().min(1, t('auth.required')),
        password: z.string().min(1, t('auth.required')),
      }),
      { sync: true }
    ),
  });
  const signIn = useMutation({
    mutationFn: async (values: { userName: string; password: string }) =>
      (await login({ body: values, throwOnError: true })).data,
    onSuccess: async (session) => {
      queryClient.setQueryData(sessionKey(), session);
      router.history.push(safeRedirect(search.redirect));
    },
  });

  return (
    <AuthCard title={t('auth.signInTitle')} text={t('auth.signInText')}>
      <form onSubmit={form.onSubmit((values) => signIn.mutate(values))}>
        <Stack gap="md">
          {signIn.isError && <ProblemAlert error={signIn.error} />}
          <TextInput
            label={t('auth.userName')}
            autoComplete="username"
            data-autofocus
            withAsterisk
            key={form.key('userName')}
            {...form.getInputProps('userName')}
          />
          <PasswordInput
            label={t('auth.password')}
            autoComplete="current-password"
            withAsterisk
            key={form.key('password')}
            {...form.getInputProps('password')}
          />
          <Button type="submit" fullWidth mt="sm" loading={signIn.isPending}>
            {t('auth.signIn')}
          </Button>
          <div>
            <Text size="xs" c="dimmed">
              {t('auth.forgot')}
            </Text>
            <Code block mt={4}>
              gamewarden owner reset-password
            </Code>
          </div>
        </Stack>
      </form>
    </AuthCard>
  );
}
