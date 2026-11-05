import { useTranslation } from 'react-i18next';
import { Anchor, Modal, Stack, Text } from '@mantine/core';
import { ReleaseNotes } from './ReleaseNotes';
import { closeUpdateNotes, useUpdateState } from './updateStore';

/** The release notes stay reachable after the success banner is closed, so "what changed?" survives the update. */
export function ReleaseNotesModal() {
  const { t } = useTranslation();
  const state = useUpdateState();
  const target = state.target;

  return (
    <Modal
      opened={state.notesOpen && !!target}
      onClose={closeUpdateNotes}
      title={target ? t('system.whatsNewTitle', { version: target.to }) : t('system.releaseNotes')}
      size="lg"
    >
      <Stack gap="sm">
        {target?.notes?.trim() ? (
          <ReleaseNotes notes={target.notes} maxHeight={420} />
        ) : (
          <Text size="sm" c="dimmed">
            {t('system.noNotes')}
          </Text>
        )}
        {target?.url && (
          <Anchor href={target.url} target="_blank" rel="noreferrer" size="sm">
            {t('system.openRelease')}
          </Anchor>
        )}
      </Stack>
    </Modal>
  );
}
