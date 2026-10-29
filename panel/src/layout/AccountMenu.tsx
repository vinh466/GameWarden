import { IconKey, IconLogout, IconUserCircle } from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ActionIcon, Menu, Modal } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { logout } from '@/api/generated';
import { sessionQuery } from '@/api/session';
import { useHelpHost } from '@/components/help/useHelp';
import { ChangePasswordForm } from '@/features/auth/ChangePasswordForm';

/**
 * The signed-in user: change password, sign out (docs/11 §2 "Access"). The password form is a Modal of its own, so it
 * can step aside for the help drawer (docs/12 §4).
 */
export function AccountMenu() {
  const { t } = useTranslation();
  const router = useRouter();
  const queryClient = useQueryClient();
  const session = useQuery(sessionQuery());
  const [changing, change] = useDisclosure(false);
  const helpHost = useHelpHost();
  const signOut = useMutation({
    mutationFn: () => logout({ throwOnError: true }),
    onSettled: async () => {
      queryClient.clear(); // nothing of this user stays in the browser
      await router.navigate({ to: '/login' });
    },
  });

  return (
    <>
      <Modal
        opened={changing}
        onClose={change.close}
        title={t('auth.changePassword')}
        {...helpHost}
      >
        <ChangePasswordForm onDone={change.close} />
      </Modal>
      <Menu position="bottom-end" width={220}>
        <Menu.Target>
          <ActionIcon size="lg" aria-label={t('auth.account')}>
            <IconUserCircle size={20} />
          </ActionIcon>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Label>{t('auth.signedInAs', { user: session.data?.userName ?? '' })}</Menu.Label>
          <Menu.Item leftSection={<IconKey size={16} />} onClick={change.open}>
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
    </>
  );
}
