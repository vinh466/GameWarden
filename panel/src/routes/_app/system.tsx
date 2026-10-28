import { useQuery } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { SimpleGrid, Skeleton, Stack } from '@mantine/core';
import { queries } from '@/api/queries';
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
        <UpdatesCard system={data} />
        <SimpleGrid cols={{ base: 1, md: 2 }}>
          <PrerequisitesCard prerequisites={data.prerequisites} />
          <NetworkCard hosts={data.network} vpnRanges={data.vpnRanges} />
        </SimpleGrid>
        <StorageCard system={data} />
      </Stack>
    </>
  );
}
