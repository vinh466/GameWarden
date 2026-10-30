import { useState } from 'react';
import { IconAlertCircle, IconLoader2 } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Alert, Button, Group, Stack, Text } from '@mantine/core';
import type { ModSetView } from '@/api/generated';
import { queries } from '@/api/queries';
import { JobProgress } from '@/components/JobProgress';
import { JobLogDrawer } from '@/features/jobs/JobLogDrawer';

/**
 * The newest set that is still building or failed (docs/13 §4.2): its job's progress or message, and its log. The active
 * set stays in use either way.
 */
export function PendingSet({ set }: { set: ModSetView }) {
  const { t } = useTranslation();
  const [logOpen, setLogOpen] = useState(false);
  const job = useQuery({ ...queries.job(set.jobId ?? ''), enabled: !!set.jobId });
  const building = set.state === 'building';

  return (
    <>
      <Alert
        color={building ? 'yellow' : 'red'}
        icon={building ? <IconLoader2 aria-hidden /> : <IconAlertCircle aria-hidden />}
        title={building ? t('mods.building') : t('mods.failed')}
      >
        <Stack gap="xs">
          <Text size="sm">{building ? t('mods.buildingText') : t('mods.failedText')}</Text>
          {building && job.data && <JobProgress job={job.data} />}
          {!building && job.data?.message && (
            <Text size="sm" c="red">
              {job.data.message}
            </Text>
          )}
          {set.jobId && (
            <Group>
              <Button
                size="xs"
                variant="light"
                color={building ? 'yellow' : 'red'}
                onClick={() => setLogOpen(true)}
              >
                {t('mods.viewLog')}
              </Button>
            </Group>
          )}
        </Stack>
      </Alert>
      <JobLogDrawer jobId={logOpen ? set.jobId : null} onClose={() => setLogOpen(false)} />
    </>
  );
}
