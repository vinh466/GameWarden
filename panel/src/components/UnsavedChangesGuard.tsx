import { Block, useRouter, type AnyRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Button, Group, Modal, Text } from '@mantine/core';

/**
 * Blocks a route change and a tab close while a form has unsaved edits (docs/22 §11.1, B3). The caller computes
 * `active` from its own dirty state, so the guard has no opinion about the shape of the form.
 */
export function UnsavedChangesGuard({ active }: { active: boolean }) {
  // Unit tests render forms without a router; there is no navigation to block there, so stay inert.
  const router = useRouter({ warn: false }) as unknown as AnyRouter | undefined;
  return router ? <RouterGuard active={active} /> : null;
}

function RouterGuard({ active }: { active: boolean }) {
  const { t } = useTranslation();
  return (
    <Block shouldBlockFn={() => active} enableBeforeUnload={() => active} withResolver>
      {({ status, proceed, reset }) => (
        <Modal
          opened={status === 'blocked'}
          onClose={() => reset?.()}
          title={t('unsaved.title')}
          centered
          zIndex={300}
        >
          <Text size="sm">{t('unsaved.text')}</Text>
          <Group justify="flex-end" mt="lg">
            <Button variant="default" onClick={() => reset?.()}>
              {t('unsaved.stay')}
            </Button>
            <Button color="red" onClick={() => proceed?.()}>
              {t('unsaved.leave')}
            </Button>
          </Group>
        </Modal>
      )}
    </Block>
  );
}
