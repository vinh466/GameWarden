import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Badge, Button, Group, Skeleton, Stack, Text } from '@mantine/core';
import { queries } from '@/api/queries';
import { PrerequisitesCard } from '@/features/system/PrerequisitesCard';

/**
 * Setup step 2 (docs/11 §2 "Access"): what the games need on this machine, with download links (installs stay
 * links in M2, D5), and whether the download hosts resolve (a blocked one is the DNS problem of docs/08 §2).
 */
export function PrerequisitesStep({ onNext }: { onNext: () => void }) {
  const { t } = useTranslation();
  const system = useQuery(queries.system());
  if (!system.data) {
    return <Skeleton h={200} />;
  }

  const blocked = system.data.network.filter((host) => host.status !== 'ok');
  return (
    <Stack gap="md">
      <Text size="sm" c="dimmed">
        {t('auth.prerequisitesText')}
      </Text>
      <PrerequisitesCard prerequisites={system.data.prerequisites} />
      <Group gap="xs">
        <Text size="sm">{t('wizard.dns')}:</Text>
        {blocked.length === 0 ? (
          <Badge color="green">{t('system.dnsOk')}</Badge>
        ) : (
          blocked.map((host) => (
            <Badge key={host.host} color="red" tt="none">
              {host.host}
            </Badge>
          ))
        )}
      </Group>
      {blocked.length > 0 && (
        <Text size="xs" c="red">
          {t('auth.dnsBlockedHint')}
        </Text>
      )}
      <Group justify="flex-end">
        <Button onClick={onNext}>{t('actions.next')}</Button>
      </Group>
    </Stack>
  );
}
