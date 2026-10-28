import { IconAlertCircle } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Alert, Button } from '@mantine/core';
import { toProblem } from '@/api/problems';

/** A failed query: the API's title and a retry (docs/11 §5, empty / loading / error). */
export function ProblemAlert({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const { t } = useTranslation();
  const problem = toProblem(error, t('errors.generic'));
  return (
    <Alert color="red" icon={<IconAlertCircle aria-hidden />} title={problem.title}>
      {onRetry && (
        <Button size="xs" variant="light" color="red" onClick={onRetry}>
          {t('actions.retry')}
        </Button>
      )}
    </Alert>
  );
}
