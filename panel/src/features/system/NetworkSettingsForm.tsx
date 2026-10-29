import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Group, Skeleton, Stack, TagsInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { updateSystemSettings, type SystemSettingsView } from '@/api/generated';
import { queries } from '@/api/queries';
import { HelpField } from '@/components/help/HelpLabel';
import { ProblemAlert } from '@/components/ProblemAlert';

/**
 * Who may reach GameWarden (docs/11 §13): the networks the panel answers on and the remote ranges the game ports
 * allow. An empty list means the default (the VPN ranges). Core refuses a change that would lock this browser out.
 */
export function NetworkSettingsForm({
  submitLabel,
  onSaved,
}: {
  submitLabel?: string;
  onSaved?: (settings: SystemSettingsView) => void;
}) {
  const settings = useQuery(queries.systemSettings());
  return settings.data ? (
    <Editor settings={settings.data} submitLabel={submitLabel} onSaved={onSaved} />
  ) : (
    <Skeleton h={160} />
  );
}

function Editor({
  settings,
  submitLabel,
  onSaved,
}: {
  settings: SystemSettingsView;
  submitLabel?: string;
  onSaved?: (settings: SystemSettingsView) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const defaults = settings.defaultVpnRanges.join(', ');
  const form = useForm({
    mode: 'controlled',
    initialValues: {
      panelAccessNetworks: settings.panelAccessNetworksDefault ? [] : settings.panelAccessNetworks,
      vpnRanges: settings.vpnRangesDefault ? [] : settings.vpnRanges,
    },
  });
  const save = useMutation({
    mutationFn: async (values: { panelAccessNetworks: string[]; vpnRanges: string[] }) =>
      (await updateSystemSettings({ body: values, throwOnError: true })).data,
    onSuccess: async (saved) => {
      queryClient.setQueryData(queries.systemSettings().queryKey, saved);
      await queryClient.invalidateQueries({ queryKey: queries.system().queryKey });
      notifications.show({ color: 'green', message: t('system.networkSaved') });
      onSaved?.(saved);
    },
  });

  return (
    <form onSubmit={form.onSubmit((values) => save.mutate(values))}>
      <Stack gap="md">
        {save.isError && <ProblemAlert error={save.error} />}
        <HelpField label={t('system.panelNetworks')} help={{ topicId: 'system.panelNetworks' }}>
          {(id) => (
            <TagsInput
              id={id}
              description={t('system.panelNetworksHint')}
              placeholder={t('system.rangesPlaceholder', { ranges: t('system.sameAsVpn') })}
              splitChars={[',', ' ']}
              clearable
              {...form.getInputProps('panelAccessNetworks')}
            />
          )}
        </HelpField>
        <HelpField label={t('system.vpnRanges')} help={{ topicId: 'system.vpnRanges' }}>
          {(id) => (
            <TagsInput
              id={id}
              description={t('system.vpnRangesHint')}
              placeholder={t('system.rangesPlaceholder', { ranges: defaults })}
              splitChars={[',', ' ']}
              clearable
              {...form.getInputProps('vpnRanges')}
            />
          )}
        </HelpField>
        <Group justify="flex-end">
          <Button type="submit" loading={save.isPending}>
            {submitLabel ?? t('actions.save')}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
