import type { ReactNode } from 'react';
import { IconAlertTriangle, IconCheck, IconX } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Group, Stack, Text, ThemeIcon } from '@mantine/core';
import type { AdapterSummary, SystemInfo } from '@/api/generated';
import { HelpButton } from '@/components/help/HelpButton';

/** Section 4: every check of 08 §2 with ✓ / ⚠ / ✕; the Owner may continue past warnings (docs/11 §4). */
export function ReviewChecks({ adapter, system }: { adapter: AdapterSummary; system: SystemInfo }) {
  const { t } = useTranslation();
  const min = adapter.minResources ?? {};
  const recommended = adapter.recommendedResources ?? {};
  const machine = system.machine;
  const level = (have: number, need?: number | null, want?: number | null) =>
    need != null && have < need ? 'block' : want != null && have < want ? 'warn' : 'ok';
  // An internal port (Palworld's REST port) is for Core only; it gets no firewall rule (ADR-0022 decision 4).
  const ports = adapter.ports
    .filter((port) => !port.internal)
    .map((port) => `${port.defaultPort}/${port.protocol}`)
    .join(', ');
  const diskNeeded = min.diskMb != null ? Math.round(min.diskMb * 1.2) : null;
  const steamHosts = system.network.filter((host) => host.host.includes('steam'));

  return (
    <Stack gap="xs">
      <Check
        level="ok"
        text={t('wizard.portsAuto', { ports })}
        help={<HelpButton label={t('wizard.ports')} help={{ topicId: 'wizard.portsAuto' }} />}
      />
      <Check
        level={level(machine.cpuCores, min.cpuCores, recommended.cpuCores)}
        text={t('wizard.cpu', {
          min: min.cpuCores ?? '—',
          recommended: recommended.cpuCores ?? '—',
          have: machine.cpuCores,
        })}
      />
      <Check
        level={level(machine.ramFreeMb, min.ramMb, recommended.ramMb)}
        text={t('wizard.ram', {
          min: min.ramMb ?? '—',
          recommended: recommended.ramMb ?? '—',
          have: machine.ramFreeMb,
        })}
      />
      <Check
        level={level(machine.diskFreeMb, diskNeeded)}
        text={t('wizard.disk', { min: diskNeeded ?? '—', have: machine.diskFreeMb })}
      />
      {adapter.prerequisites.map((id) => {
        const prerequisite = system.prerequisites.find((candidate) => candidate.id === id);
        return (
          <Check
            key={id}
            level={prerequisite?.installed ? 'ok' : 'block'}
            text={`${id}: ${prerequisite?.installed ? t('system.installed') : t('system.missing')}`}
          />
        );
      })}
      <Check
        level={steamHosts.some((host) => host.status !== 'ok') ? 'warn' : 'ok'}
        text={`${t('wizard.dns')}: ${steamHosts.map((host) => `${host.host} ${host.status}`).join(', ')}`}
      />
    </Stack>
  );
}

function Check({
  level,
  text,
  help,
}: {
  level: 'ok' | 'warn' | 'block';
  text: ReactNode;
  help?: ReactNode;
}) {
  const look = {
    ok: { color: 'green', icon: IconCheck },
    warn: { color: 'yellow', icon: IconAlertTriangle },
    block: { color: 'red', icon: IconX },
  }[level];
  const Icon = look.icon;
  return (
    <Group gap="sm" wrap="nowrap" align="flex-start">
      <ThemeIcon size={22} radius="xl" color={look.color} variant="light">
        <Icon size={14} />
      </ThemeIcon>
      <Group gap={4} wrap="nowrap">
        <Text size="sm">{text}</Text>
        {help}
      </Group>
    </Group>
  );
}
