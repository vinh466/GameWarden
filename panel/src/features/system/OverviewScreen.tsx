import { IconPuzzle } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, SimpleGrid, Skeleton, Stack } from '@mantine/core';
import { queries } from '@/api/queries';
import { AnchorLink } from '@/components/links';
import { ProblemAlert } from '@/components/ProblemAlert';
import { SystemTab } from '@/features/system/SystemTab';
import { NetworkCard } from './NetworkCard';
import { PrerequisitesCard } from './PrerequisitesCard';
import { StorageCard } from './StorageCard';
import { UpdatesCard } from './UpdatesCard';

/** The machine's version, prerequisites, network and storage (docs/11 §4). */
export function OverviewScreen() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());

  if (system.isError) {
    return (
      <SystemTab description={t('system.overviewHint')}>
        <ProblemAlert error={system.error} onRetry={() => void system.refetch()} />
      </SystemTab>
    );
  }

  if (!system.data) {
    return (
      <SystemTab description={t('system.overviewHint')}>
        <Skeleton h={320} />
      </SystemTab>
    );
  }

  const data = system.data;
  return (
    <SystemTab description={t('system.overviewHint')}>
      <Stack gap="lg">
        {data.adapterProblems > 0 && (
          <Alert
            color="yellow"
            icon={<IconPuzzle aria-hidden />}
            title={t('system.adapterProblems', { problems: data.adapterProblems })}
          >
            <AnchorLink to="/system/adapters" size="sm">
              {t('system.viewAdapters')}
            </AnchorLink>
          </Alert>
        )}
        <UpdatesCard system={data} />
        <SimpleGrid cols={{ base: 1, md: 2 }}>
          <PrerequisitesCard prerequisites={data.prerequisites} />
          <NetworkCard hosts={data.network} />
        </SimpleGrid>
        <StorageCard system={data} />
      </Stack>
    </SystemTab>
  );
}
