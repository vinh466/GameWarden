import { IconPuzzle } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Alert, SimpleGrid, Skeleton, Stack } from '@mantine/core';
import { queries } from '@/api/queries';
import { AnchorLink } from '@/components/links';
import { PageHeader } from '@/components/PageHeader';
import { NetworkCard } from '@/features/system/NetworkCard';
import { PrerequisitesCard } from '@/features/system/PrerequisitesCard';
import { StorageCard } from '@/features/system/StorageCard';
import { UpdatesCard } from '@/features/system/UpdatesCard';

export const Route = createFileRoute('/_app/system')({
  loader: ({ context: { queryClient } }) =>
    queryClient.query({ ...queries.system(), staleTime: 'static' }),
  component: System,
});

function System() {
  const { t } = useTranslation();
  const system = useQuery(queries.system());
  if (!system.data) {
    return <Skeleton h={320} />;
  }

  const data = system.data;
  return (
    <>
      <PageHeader title={t('system.title')} />
      <Stack gap="lg">
        {data.adapterProblems > 0 && (
          <Alert
            color="yellow"
            icon={<IconPuzzle aria-hidden />}
            title={t('system.adapterProblems', { problems: data.adapterProblems })}
          >
            <AnchorLink to="/adapters" size="sm">
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
    </>
  );
}
