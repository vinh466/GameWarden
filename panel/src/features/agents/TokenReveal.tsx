import dayjs from 'dayjs';
import { IconAlertTriangle, IconCopy } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Code, Group, Stack, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import type { AgentTokenCreated } from '@/api/generated';

/** The plaintext token is shown once; copying is the only way it leaves this screen (docs/15). */
export function TokenReveal({ token }: { token: AgentTokenCreated }) {
  const { t } = useTranslation();
  const copy = () => {
    void navigator.clipboard
      .writeText(token.token)
      .then(() => notifications.show({ color: 'green', message: t('agents.copied') }))
      .catch(() =>
        notifications.show({
          color: 'red',
          title: t('feedback.failed'),
          message: t('agents.copyFailed'),
        })
      );
  };

  return (
    <Stack>
      <Alert color="yellow" icon={<IconAlertTriangle size={18} />}>
        {t('agents.tokenOnce')}
      </Alert>
      <Code block style={{ userSelect: 'all', wordBreak: 'break-all' }}>
        {token.token}
      </Code>
      <Group justify="flex-end">
        <Button leftSection={<IconCopy size={16} />} onClick={copy}>
          {t('agents.copy')}
        </Button>
      </Group>
      <Text size="sm" c="dimmed">
        {t('agents.tokenHelp', { expires: dayjs(token.tokenInfo.expiresAt).format('LLL') })}
      </Text>
    </Stack>
  );
}
