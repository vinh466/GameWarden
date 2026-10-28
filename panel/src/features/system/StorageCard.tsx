import { useTranslation } from 'react-i18next';
import { Card, SimpleGrid, Table, Title } from '@mantine/core';
import type { SystemInfo } from '@/api/generated';
import { ResourceMeter } from '@/components/ResourceMeter';

/** Where data lives, who games run as, free disk and RAM. */
export function StorageCard({ system }: { system: SystemInfo }) {
  const { t } = useTranslation();
  const { machine } = system;
  return (
    <Card>
      <Title order={5} mb="sm">
        {t('system.storage')}
      </Title>
      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
        <Table withRowBorders={false} highlightOnHover={false} verticalSpacing={4}>
          <Table.Tbody>
            <Table.Tr>
              <Table.Td c="dimmed" fz="sm" w={180}>
                {t('system.dataRoot')}
              </Table.Td>
              <Table.Td fz="sm" ff="monospace">
                {system.dataRoot}
              </Table.Td>
            </Table.Tr>
            <Table.Tr>
              <Table.Td c="dimmed" fz="sm">
                {t('system.isolation')}
              </Table.Td>
              <Table.Td fz="sm" ff="monospace">
                {system.isolation ? system.runAsUser : t('system.isolationOff')}
              </Table.Td>
            </Table.Tr>
            <Table.Tr>
              <Table.Td c="dimmed" fz="sm">
                {t('dashboard.diskFree')}
              </Table.Td>
              <Table.Td fz="sm" ff="monospace">
                {(machine.diskFreeMb / 1024).toFixed(1)} GB
              </Table.Td>
            </Table.Tr>
          </Table.Tbody>
        </Table>
        <ResourceMeter
          label={t('dashboard.ram')}
          used={machine.ramTotalMb - machine.ramFreeMb}
          total={machine.ramTotalMb}
          unit="MB"
        />
      </SimpleGrid>
    </Card>
  );
}
