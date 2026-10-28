import { IconKey, IconLogout, IconUserCircle } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Menu } from '@mantine/core';
import { modals } from '@mantine/modals';
import { logout } from '@/api/generated';
import { sessionQuery } from '@/api/session';
import { ChangePasswordForm } from '@/features/auth/ChangePasswordForm';

/** The signed-in user: change password, sign out (docs/11 §2 "Access"). */
export function AccountMenu() {
  const { t } = useTranslation();
  const router = useRouter();
  const queryClient = useQueryClient();
  const session = useQuery(sessionQuery());
  const signOut = useMutation({
    mutationFn: () => logout({ throwOnError: true }),
    onSettled: async () => {
      queryClient.clear(); // nothing of this user stays in the browser
      await router.navigate({ to: '/login' });
    },
  });

  return (
    <Menu position="bottom-end" width={220}>
      <Menu.Target>
        <ActionIcon size="lg" aria-label={t('auth.account')}>
          <IconUserCircle size={20} />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{t('auth.signedInAs', { user: session.data?.userName ?? '' })}</Menu.Label>
        <Menu.Item
          leftSection={<IconKey size={16} />}
          onClick={() => {
            const id = modals.open({
              title: t('auth.changePassword'),
              children: <ChangePasswordForm onDone={() => modals.close(id)} />,
            });
          }}
        >
          {t('auth.changePassword')}
        </Menu.Item>
        <Menu.Item
          leftSection={<IconLogout size={16} />}
          color="red"
          onClick={() => signOut.mutate()}
        >
          {t('auth.signOut')}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}
