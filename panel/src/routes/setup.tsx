import { useState } from 'react';
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Alert, Button, Code, Group, Stack, Stepper, Text } from '@mantine/core';
import { setupStatusQuery } from '@/api/session';
import { AuthCard } from '@/features/auth/AuthCard';
import { OwnerStep } from '@/features/setup/OwnerStep';
import { PrerequisitesStep } from '@/features/setup/PrerequisitesStep';
import { NetworkSettingsForm } from '@/features/system/NetworkSettingsForm';

// First-run setup (docs/06 §4, docs/11 §4): the installer and `gamewarden setup-url` open /setup?token=… on the
// machine itself. Owner account → prerequisites → panel access; only the first step is required.
export const Route = createFileRoute('/setup')({
  validateSearch: z.object({ token: z.string().optional() }),
  beforeLoad: async ({ context: { queryClient } }) => {
    const status = await queryClient.query(setupStatusQuery()).catch(() => null);
    if (status && !status.needsSetup) {
      throw redirect({ to: '/login' });
    }
  },
  component: Setup,
});

function Setup() {
  const { t } = useTranslation();
  const { token } = Route.useSearch();
  const router = useRouter();
  const [step, setStep] = useState(0);
  const finish = () => router.history.push('/');

  return (
    <AuthCard
      title={step === 0 ? t('auth.setupTitle') : t('auth.setupMoreTitle')}
      text={step === 0 ? t('auth.setupText') : t('auth.setupMoreText')}
      size={step === 0 ? 420 : 640}
    >
      {!token ? (
        <Alert color="yellow">
          {t('auth.setupNoToken')}
          <Code block mt="xs">
            gamewarden setup-url
          </Code>
        </Alert>
      ) : (
        <Stack gap="lg">
          <Stepper active={step} size="xs" allowNextStepsSelect={false}>
            <Stepper.Step label={t('auth.steps.account')} />
            <Stepper.Step label={t('auth.steps.prerequisites')} />
            <Stepper.Step label={t('auth.steps.access')} />
          </Stepper>
          {step === 0 && <OwnerStep token={token} onCreated={() => setStep(1)} />}
          {step === 1 && <PrerequisitesStep onNext={() => setStep(2)} />}
          {step === 2 && (
            <Stack gap="md">
              <Text size="sm" c="dimmed">
                {t('auth.accessText')}
              </Text>
              <NetworkSettingsForm submitLabel={t('auth.saveAndFinish')} onSaved={finish} />
              <Group justify="center">
                <Button variant="subtle" onClick={finish}>
                  {t('auth.keepDefaults')}
                </Button>
              </Group>
            </Stack>
          )}
        </Stack>
      )}
    </AuthCard>
  );
}
