import { useTranslation } from 'react-i18next';
import { Select, SimpleGrid, Text } from '@mantine/core';
import type { NotificationDestinationUpdate } from '@/api/generated';
import { HelpField } from '@/components/help/HelpLabel';
import { DestinationOption } from './DestinationOption';
import { notificationSeverities } from './notifications';

/** How a destination reports: mode, language and the lowest severity it sends. */
export function DestinationRoutingFields({
  value,
  set,
  modes,
}: {
  value: NotificationDestinationUpdate;
  set: <K extends keyof NotificationDestinationUpdate>(
    key: K,
    next: NotificationDestinationUpdate[K]
  ) => void;
  modes: string[];
}) {
  const { t } = useTranslation();
  const modeLabels: Record<string, string> = {
    instant: t('system.notifications.modes.instant'),
    digest: t('system.notifications.modes.digest'),
    status: t('system.notifications.modes.status'),
  };
  const modeHelp: Record<string, string> = {
    instant: t('system.notifications.modeHelp.instant'),
    digest: t('system.notifications.modeHelp.digest'),
    status: t('system.notifications.modeHelp.status'),
  };
  const modeData = modes.map((mode) => ({
    value: mode,
    label: modeLabels[mode] ?? mode,
  }));
  const languageHelp: Record<string, string> = {
    vi: t('system.notifications.languageHelp.vi'),
    en: t('system.notifications.languageHelp.en'),
  };
  const languageData = [
    { value: 'vi', label: 'Tiếng Việt' },
    { value: 'en', label: 'English' },
  ];
  const severityLabels: Record<string, string> = {
    info: t('system.notifications.severities.info'),
    success: t('system.notifications.severities.success'),
    warning: t('system.notifications.severities.warning'),
    error: t('system.notifications.severities.error'),
    critical: t('system.notifications.severities.critical'),
  };
  const severityHelp: Record<string, string> = {
    info: t('system.notifications.severityHelp.info'),
    success: t('system.notifications.severityHelp.success'),
    warning: t('system.notifications.severityHelp.warning'),
    error: t('system.notifications.severityHelp.error'),
    critical: t('system.notifications.severityHelp.critical'),
  };
  const severityData = notificationSeverities.map((severity) => ({
    value: severity,
    label: severityLabels[severity] ?? severity,
  }));

  return (
    <>
      <SimpleGrid cols={{ base: 1, sm: 3 }}>
        <HelpField label={t('system.notifications.mode')} help={{ topicId: 'notifications.mode' }}>
          {(id) => (
            <Select
              id={id}
              data={modeData}
              value={value.mode}
              onChange={(next) => set('mode', next ?? 'instant')}
              allowDeselect={false}
              renderOption={({ option }) => (
                <DestinationOption label={option.label} description={modeHelp[option.value]} />
              )}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.language')}
          help={{ topicId: 'notifications.language' }}
        >
          {(id) => (
            <Select
              id={id}
              data={languageData}
              value={value.language}
              onChange={(next) => set('language', next ?? 'vi')}
              allowDeselect={false}
              renderOption={({ option }) => (
                <DestinationOption label={option.label} description={languageHelp[option.value]} />
              )}
            />
          )}
        </HelpField>
        <HelpField
          label={t('system.notifications.minimumSeverity')}
          help={{ topicId: 'notifications.minimumSeverity' }}
        >
          {(id) => (
            <Select
              id={id}
              data={severityData}
              value={value.minimumSeverity}
              onChange={(next) => set('minimumSeverity', next ?? 'warning')}
              allowDeselect={false}
              renderOption={({ option }) => (
                <DestinationOption label={option.label} description={severityHelp[option.value]} />
              )}
            />
          )}
        </HelpField>
      </SimpleGrid>
      {value.mode === 'digest' && (
        <Text size="xs" c="dimmed">
          {t('system.notifications.modeDigestHint')}
        </Text>
      )}
      {value.mode === 'status' && (
        <Text size="xs" c="dimmed">
          {t('system.notifications.modeStatusHint')}
        </Text>
      )}
    </>
  );
}
