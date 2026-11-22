import { useEffect, useMemo, useRef, useState } from 'react';
import { IconAlertTriangle } from '@tabler/icons-react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Code,
  Group,
  Modal,
  Radio,
  Select,
  SimpleGrid,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import {
  createAgentClient,
  createAgentToken,
  type AgentClientView,
  type AgentPermissionInfo,
  type AgentPermissionPreset,
  type AgentTokenCreated,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import classes from './CreateAgentModal.module.css';

/** Creates a client and its first token in one step; the token is revealed once (docs/15). */
export function CreateAgentModal({
  opened,
  onClose,
  permissions,
  presets,
  onCreated,
}: {
  opened: boolean;
  onClose: () => void;
  permissions: AgentPermissionInfo[];
  presets: AgentPermissionPreset[];
  onCreated: (client: AgentClientView, token: AgentTokenCreated) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const isPhone = useMediaQuery('(max-width: 48em)');
  const [name, setName] = useState('opencode-devbox');
  const [description, setDescription] = useState('');
  const [preset, setPreset] = useState('operator');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [trustedFull, setTrustedFull] = useState(false);
  const [networks, setNetworks] = useState('');
  const [days, setDays] = useState('30');
  const initialized = useRef(false);
  useEffect(() => {
    const operator = presets.find((item) => item.key === 'operator');
    if (!initialized.current && operator) {
      initialized.current = true;
      setSelected(new Set(operator.permissions));
    }
  }, [presets]);

  const groups = useMemo(() => {
    const map = new Map<string, AgentPermissionInfo[]>();
    for (const permission of permissions) {
      const list = map.get(permission.group) ?? [];
      list.push(permission);
      map.set(permission.group, list);
    }

    return [...map.entries()];
  }, [permissions]);

  const create = useMutation({
    mutationFn: async () => {
      const chosen = trustedFull ? permissions.map((permission) => permission.key) : [...selected];
      const client = await createAgentClient({
        body: {
          name,
          description: description || null,
          permissions: chosen,
          trustedFull,
          allowedNetworks: networks
            .split(/\s+/)
            .map((value) => value.trim())
            .filter(Boolean),
        },
        throwOnError: true,
      });
      const token = await createAgentToken({
        path: { id: client.data.id },
        body: { name: 'panel', expiresInDays: Number(days) },
        throwOnError: true,
      });
      return { client: client.data, token: token.data };
    },
    onSuccess: ({ client, token }) => {
      void queryClient.invalidateQueries();
      onCreated(client, token);
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const applyPreset = (key: string | null) => {
    setPreset(key ?? 'custom');
    const chosen = presets.find((item) => item.key === key)?.permissions;
    if (chosen) {
      setSelected(new Set(chosen));
    }
  };

  const toggle = (key: string, checked: boolean) => {
    setPreset('custom');
    setSelected((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(key);
      } else {
        next.delete(key);
      }

      return next;
    });
  };

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title={t('agents.createTitle')}
      size="xl"
      fullScreen={isPhone}
    >
      <Stack>
        <TextInput
          label={t('agents.name')}
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
          required
        />
        <Radio.Group label={t('agents.preset')} value={preset} onChange={applyPreset}>
          <SimpleGrid cols={{ base: 1, sm: 2 }} mt="xs">
            {presets.map((item) => (
              <Radio.Card key={item.key} value={item.key} className={classes.preset} radius="md">
                <Group wrap="nowrap" align="flex-start">
                  <Radio.Indicator mt={2} />
                  <Stack gap={2}>
                    <Group gap="xs" justify="space-between" wrap="nowrap">
                      <Text size="sm" fw={600}>
                        {item.name}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {t('agents.presetPermissions', { count: item.permissions.length })}
                      </Text>
                    </Group>
                    <Text size="xs" c="dimmed">
                      {item.description}
                    </Text>
                  </Stack>
                </Group>
              </Radio.Card>
            ))}
            <Radio.Card value="custom" className={classes.preset} radius="md">
              <Group wrap="nowrap" align="flex-start">
                <Radio.Indicator mt={2} />
                <Stack gap={2}>
                  <Text size="sm" fw={600}>
                    {t('agents.customPreset')}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {t('agents.customPresetText')}
                  </Text>
                </Stack>
              </Group>
            </Radio.Card>
          </SimpleGrid>
        </Radio.Group>
        <TextInput
          label={t('agents.descriptionLabel')}
          value={description}
          onChange={(event) => setDescription(event.currentTarget.value)}
        />
        <Switch
          label={t('agents.trustedFull')}
          description={t('agents.trustedFullText')}
          checked={trustedFull}
          onChange={(event) => setTrustedFull(event.currentTarget.checked)}
          color="red"
        />
        {!trustedFull && (
          <Stack gap="sm">
            <Group justify="space-between" gap="xs">
              <Text size="sm" fw={500}>
                {t('agents.permissions')}
              </Text>
              <Text size="xs" c="dimmed">
                {t('agents.permissionCount', {
                  selected: selected.size,
                  total: permissions.length,
                })}
              </Text>
            </Group>
            {groups.map(([group, list]) => (
              <Stack key={group} gap={4}>
                <Group justify="space-between" gap="xs">
                  <Text size="xs" tt="uppercase" c="dimmed" fw={700}>
                    {group}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {list.filter((permission) => selected.has(permission.key)).length}/{list.length}
                  </Text>
                </Group>
                {list.map((permission) => (
                  <Checkbox
                    key={permission.key}
                    checked={selected.has(permission.key)}
                    onChange={(event) => toggle(permission.key, event.currentTarget.checked)}
                    label={
                      <Group gap="xs">
                        <Code>{permission.key}</Code>
                        <Badge size="xs" variant="light" color={riskColor(permission.risk)}>
                          {permission.risk}
                        </Badge>
                        <Text size="xs" c="dimmed">
                          {permission.description}
                        </Text>
                      </Group>
                    }
                  />
                ))}
              </Stack>
            ))}
          </Stack>
        )}
        <Group grow align="flex-start">
          <Textarea
            label={t('agents.allowedNetworks')}
            description={t('agents.allowedNetworksText')}
            placeholder="100.64.0.10/32"
            value={networks}
            onChange={(event) => setNetworks(event.currentTarget.value)}
            minRows={2}
          />
          <Select
            label={t('agents.expiry')}
            value={days}
            onChange={(value) => setDays(value ?? '30')}
            data={[
              { value: '1', label: t('agents.oneDay') },
              { value: '7', label: t('agents.sevenDays') },
              { value: '30', label: t('agents.thirtyDays') },
              { value: '90', label: t('agents.ninetyDays') },
            ]}
          />
        </Group>
        <Alert color="blue" icon={<IconAlertTriangle size={18} />}>
          {t('agents.tokenOnce')}
        </Alert>
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            {t('actions.cancel')}
          </Button>
          <Button
            onClick={() => create.mutate()}
            loading={create.isPending}
            disabled={!name.trim() || (!trustedFull && selected.size === 0)}
          >
            {t('agents.create')}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function riskColor(risk: string): string {
  return risk === 'critical'
    ? 'red'
    : risk === 'high'
      ? 'orange'
      : risk === 'medium'
        ? 'yellow'
        : 'green';
}
