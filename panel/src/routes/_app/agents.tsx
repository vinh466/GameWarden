import { useEffect, useMemo, useRef, useState } from 'react';
import {
  IconAlertTriangle,
  IconCopy,
  IconKey,
  IconPlus,
  IconRobot,
  IconShieldCheck,
} from '@tabler/icons-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createFileRoute } from '@tanstack/react-router';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Badge,
  Button,
  Card,
  Checkbox,
  Code,
  Divider,
  Group,
  Modal,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import {
  createAgentClient,
  createAgentToken,
  revokeAgentToken,
  updateAgentClient,
  updateAgentSettings,
  type AgentClientView,
  type AgentPermissionInfo,
  type AgentSettingsView,
  type AgentTokenCreated,
  type UpdateAgentSettingsRequest,
} from '@/api/generated';
import { toProblem } from '@/api/problems';
import { queries } from '@/api/queries';
import { PageHeader } from '@/components/PageHeader';

export const Route = createFileRoute('/_app/agents')({
  component: Agents,
});

function Agents() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const settings = useQuery(queries.agentSettings());
  const catalog = useQuery(queries.agentPermissions());
  const clients = useQuery(queries.agents());
  const audit = useQuery(queries.agentAudit());
  const [createOpen, create] = useDisclosure(false);
  const [token, setToken] = useState<AgentTokenCreated | null>(null);
  const [tokenClient, setTokenClient] = useState<AgentClientView | null>(null);
  const [mode, setMode] = useState<'diagnostics' | 'admin' | null>(null);
  const [modeReason, setModeReason] = useState('debug session');
  const [modeMinutes, setModeMinutes] = useState('30');

  const refresh = () => void queryClient.invalidateQueries();

  const settingsMutation = useMutation({
    mutationFn: (body: UpdateAgentSettingsRequest) =>
      updateAgentSettings({ body, throwOnError: true }),
    onSuccess: refresh,
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  const clientMutation = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
      updateAgentClient({
        path: { id },
        body: {
          name: null,
          description: null,
          enabled,
          permissions: null,
          trustedFull: null,
          allowedNetworks: null,
        },
        throwOnError: true,
      }),
    onSuccess: refresh,
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  if (!settings.data || !clients.data || !catalog.data) {
    return <Text c="dimmed">{t('agents.loading')}</Text>;
  }

  const current = settings.data;
  const setEnabled = (enabled: boolean) =>
    settingsMutation.mutate({
      enabled,
      requireAdminMode: null,
      diagnostics: null,
      admin: null,
      allowedNetworks: null,
    });

  const openMode = (next: 'diagnostics' | 'admin') => {
    setMode(next);
    setModeReason(next === 'diagnostics' ? 'debug session' : 'maintenance');
    setModeMinutes(next === 'diagnostics' ? '30' : '15');
  };

  const enableMode = () => {
    if (!mode) {
      return;
    }

    settingsMutation.mutate(
      {
        enabled: null,
        requireAdminMode: null,
        diagnostics:
          mode === 'diagnostics'
            ? { enabled: true, ttlMinutes: Number(modeMinutes), reason: modeReason }
            : null,
        admin:
          mode === 'admin'
            ? { enabled: true, ttlMinutes: Number(modeMinutes), reason: modeReason }
            : null,
        allowedNetworks: null,
      },
      {
        onSuccess: () => {
          setMode(null);
          notifications.show({ color: 'green', message: t('agents.modeEnabled') });
        },
      },
    );
  };

  return (
    <>
      <PageHeader
        title={t('agents.title')}
        description={t('agents.description')}
        actions={
          <Button leftSection={<IconPlus size={16} />} onClick={create.open}>
            {t('agents.create')}
          </Button>
        }
      />
      <Stack gap="lg">
        <FeatureCard
          settings={current}
          busy={settingsMutation.isPending}
          onToggle={setEnabled}
          onMode={openMode}
          onDisableMode={(which) =>
            settingsMutation.mutate({
              enabled: null,
              requireAdminMode: null,
              diagnostics: which === 'diagnostics' ? { enabled: false, ttlMinutes: null, reason: null } : null,
              admin: which === 'admin' ? { enabled: false, ttlMinutes: null, reason: null } : null,
              allowedNetworks: null,
            })
          }
        />

        <Card>
          <Group justify="space-between" mb="sm">
            <Title order={5}>{t('agents.clients')}</Title>
            <Badge variant="light">{clients.data.length}</Badge>
          </Group>
          {clients.data.length === 0 ? (
            <Text size="sm" c="dimmed">
              {t('agents.empty')}
            </Text>
          ) : (
            <ScrollArea>
              <Table highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('agents.name')}</Table.Th>
                    <Table.Th>{t('agents.status')}</Table.Th>
                    <Table.Th>{t('agents.permissions')}</Table.Th>
                    <Table.Th>{t('agents.lastSeen')}</Table.Th>
                    <Table.Th />
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {clients.data.map((client) => (
                    <Table.Tr key={client.id}>
                      <Table.Td>
                        <Stack gap={0}>
                          <Text size="sm" fw={500}>
                            {client.name}
                          </Text>
                          <Text size="xs" c="dimmed" ff="monospace">
                            {client.id}
                          </Text>
                        </Stack>
                      </Table.Td>
                      <Table.Td>
                        <Badge color={client.enabled ? 'green' : 'gray'} variant="light">
                          {client.enabled ? t('agents.enabled') : t('agents.disabled')}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm">
                          {client.trustedFull
                            ? t('agents.trustedFull')
                            : t('agents.permissionCount', { count: client.permissions.length })}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm" c="dimmed">
                          {client.lastSeenAt ? dayjs(client.lastSeenAt).fromNow() : t('agents.never')}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Group gap="xs" justify="flex-end" wrap="nowrap">
                          <Button
                            size="compact-xs"
                            variant="default"
                            leftSection={<IconKey size={12} />}
                            onClick={() => setTokenClient(client)}
                          >
                            {t('agents.tokens')}
                          </Button>
                          <Button
                            size="compact-xs"
                            variant={client.enabled ? 'light' : 'filled'}
                            color={client.enabled ? 'gray' : 'green'}
                            onClick={() => clientMutation.mutate({ id: client.id, enabled: !client.enabled })}
                          >
                            {client.enabled ? t('agents.disable') : t('agents.enable')}
                          </Button>
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          )}
        </Card>

        <AuditCard rows={audit.data ?? []} />
      </Stack>

      <CreateAgentModal
        opened={createOpen}
        onClose={create.close}
        permissions={catalog.data.permissions}
        onCreated={(_created, createdToken) => {
          create.close();
          refresh();
          setToken(createdToken);
          setTokenClient(null);
        }}
      />

      <Modal
        opened={token !== null}
        onClose={() => setToken(null)}
        title={t('agents.tokenTitle')}
        size="lg"
      >
        {token && <TokenReveal token={token} />}
      </Modal>

      <ClientTokensModal
        client={tokenClient}
        onClose={() => {
          setTokenClient(null);
        }}
        onRefresh={refresh}
      />

      <Modal opened={mode !== null} onClose={() => setMode(null)} title={t('agents.modeTitle')}>
        <Stack>
          <Alert color="yellow" icon={<IconAlertTriangle size={18} />}>
            {mode === 'diagnostics' ? t('agents.diagnosticsWarning') : t('agents.adminWarning')}
          </Alert>
          <TextInput
            label={t('agents.reason')}
            value={modeReason}
            onChange={(event) => setModeReason(event.currentTarget.value)}
          />
          <Select
            label={t('agents.durationMinutes')}
            value={modeMinutes}
            onChange={(value) => setModeMinutes(value ?? '30')}
            data={mode === 'admin' ? ['15', '30', '60'] : ['15', '30', '60', '120']}
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setMode(null)}>
              {t('actions.cancel')}
            </Button>
            <Button color="yellow" onClick={enableMode} loading={settingsMutation.isPending}>
              {t('agents.enableMode')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}

function FeatureCard({
  settings,
  busy,
  onToggle,
  onMode,
  onDisableMode,
}: {
  settings: AgentSettingsView;
  busy: boolean;
  onToggle: (enabled: boolean) => void;
  onMode: (mode: 'diagnostics' | 'admin') => void;
  onDisableMode: (mode: 'diagnostics' | 'admin') => void;
}) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" align="flex-start" gap="md">
        <Stack gap={4}>
          <Group gap="xs">
            <IconRobot size={20} />
            <Title order={5}>{t('agents.feature')}</Title>
          </Group>
          <Text size="sm" c="dimmed">
            {t('agents.featureText')}
          </Text>
        </Stack>
        <Switch
          checked={settings.enabled}
          onChange={(event) => onToggle(event.currentTarget.checked)}
          disabled={busy}
          label={settings.enabled ? t('agents.on') : t('agents.off')}
        />
      </Group>
      <Divider my="md" />
      <Group gap="xl" align="flex-start">
        <ModeRow
          title={t('agents.diagnostics')}
          window={settings.diagnostics}
          warning={t('agents.diagnosticsText')}
          onEnable={() => onMode('diagnostics')}
          onDisable={() => onDisableMode('diagnostics')}
        />
        <ModeRow
          title={t('agents.admin')}
          window={settings.admin}
          warning={t('agents.adminText')}
          onEnable={() => onMode('admin')}
          onDisable={() => onDisableMode('admin')}
        />
      </Group>
    </Card>
  );
}

function ModeRow({
  title,
  window,
  warning,
  onEnable,
  onDisable,
}: {
  title: string;
  window: AgentSettingsView['diagnostics'];
  warning: string;
  onEnable: () => void;
  onDisable: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap={4} style={{ flex: 1, minWidth: 240 }}>
      <Group gap="xs">
        <IconShieldCheck size={16} />
        <Text fw={500}>{title}</Text>
        <Badge color={window.enabled ? 'yellow' : 'gray'} variant="light">
          {window.enabled
            ? t('agents.modeOn', { at: window.expiresAt ? dayjs(window.expiresAt).format('HH:mm') : '—' })
            : t('agents.off')}
        </Badge>
      </Group>
      <Text size="xs" c="dimmed">
        {warning}
      </Text>
      {window.enabled ? (
        <Group gap="xs">
          <Button size="compact-xs" variant="default" onClick={onDisable}>
            {t('agents.turnOff')}
          </Button>
          {window.reason && (
            <Text size="xs" c="dimmed">
              {window.reason}
            </Text>
          )}
        </Group>
      ) : (
        <Button size="compact-xs" variant="light" color="yellow" onClick={onEnable}>
          {t('agents.turnOn')}
        </Button>
      )}
    </Stack>
  );
}

function CreateAgentModal({
  opened,
  onClose,
  permissions,
  onCreated,
}: {
  opened: boolean;
  onClose: () => void;
  permissions: AgentPermissionInfo[];
  onCreated: (client: AgentClientView, token: AgentTokenCreated) => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('opencode-devbox');
  const [description, setDescription] = useState('');
  const [preset, setPreset] = useState('operator');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [trustedFull, setTrustedFull] = useState(false);
  const [networks, setNetworks] = useState('');
  const [days, setDays] = useState('30');
  const queryClient = useQueryClient();
  const catalog = useQuery(queries.agentPermissions());
  const initialized = useRef(false);
  useEffect(() => {
    const preset = catalog.data?.presets.find((item) => item.key === 'operator');
    if (!initialized.current && preset) {
      initialized.current = true;
      setSelected(new Set(preset.permissions));
    }
  }, [catalog.data]);
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
      const chosen = trustedFull
        ? permissions.map((permission) => permission.key)
        : [...selected];
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
    const chosen = catalog.data?.presets.find((item) => item.key === key)?.permissions;
    if (chosen) {
      setSelected(new Set(chosen));
    }
  };

  const toggle = (key: string, checked: boolean) => {
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
    <Modal opened={opened} onClose={onClose} title={t('agents.createTitle')} size="xl">
      <Stack>
        <Group grow align="flex-start">
          <TextInput
            label={t('agents.name')}
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            required
          />
          <Select
            label={t('agents.preset')}
            value={preset}
            onChange={applyPreset}
            data={(catalog.data?.presets ?? []).map((item) => ({
              value: item.key,
              label: item.name,
            }))}
          />
        </Group>
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
          <Stack gap="xs">
            <Text size="sm" fw={500}>
              {t('agents.permissions')}
            </Text>
            <ScrollArea h={260} type="auto">
              {groups.map(([group, list]) => (
                <Stack key={group} gap={4} mb="sm">
                  <Text size="xs" tt="uppercase" c="dimmed" fw={700}>
                    {group}
                  </Text>
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
            </ScrollArea>
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

function TokenReveal({ token }: { token: AgentTokenCreated }) {
  const { t } = useTranslation();
  return (
    <Stack>
      <Alert color="yellow" icon={<IconAlertTriangle size={18} />}>
        {t('agents.tokenOnce')}
      </Alert>
      <Code block style={{ userSelect: 'all', wordBreak: 'break-all' }}>
        {token.token}
      </Code>
      <Group justify="flex-end">
        <Button
          leftSection={<IconCopy size={16} />}
          onClick={() =>
            void navigator.clipboard.writeText(token.token).then(() =>
              notifications.show({ color: 'green', message: t('agents.copied') }),
            )
          }
        >
          {t('agents.copy')}
        </Button>
      </Group>
      <Text size="sm" c="dimmed">
        {t('agents.tokenHelp', { expires: dayjs(token.tokenInfo.expiresAt).format('LLL') })}
      </Text>
    </Stack>
  );
}

function ClientTokensModal({
  client,
  onClose,
  onRefresh,
}: {
  client: AgentClientView | null;
  onClose: () => void;
  onRefresh: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [createdToken, setCreatedToken] = useState<AgentTokenCreated | null>(null);
  const tokens = useQuery({ ...queries.agentTokens(client?.id ?? ''), enabled: client !== null });
  const create = useMutation({
    mutationFn: () =>
      createAgentToken({
        path: { id: client!.id },
        body: { name: 'panel', expiresInDays: 30 },
        throwOnError: true,
      }),
    onSuccess: ({ data }) => {
      setCreatedToken(data);
      void queryClient.invalidateQueries();
      onRefresh();
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });
  const revoke = useMutation({
    mutationFn: (tokenId: string) =>
      revokeAgentToken({ path: { id: client!.id, tokenId }, throwOnError: true }),
    onSuccess: () => {
      void queryClient.invalidateQueries();
      onRefresh();
    },
    onError: (error) =>
      notifications.show({
        color: 'red',
        title: t('feedback.failed'),
        message: toProblem(error, t('errors.generic')).title,
      }),
  });

  return (
    <Modal opened={client !== null} onClose={onClose} title={t('agents.tokensTitle', { name: client?.name ?? '' })} size="lg">
      {createdToken && <TokenReveal token={createdToken} />}
      {createdToken && <Divider my="md" />}
      <Group justify="space-between" mb="sm">
        <Text size="sm" c="dimmed">
          {t('agents.tokensText')}
        </Text>
        <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => create.mutate()} loading={create.isPending}>
          {t('agents.newToken')}
        </Button>
      </Group>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>{t('agents.tokenName')}</Table.Th>
            <Table.Th>{t('agents.tokenExpires')}</Table.Th>
            <Table.Th>{t('agents.lastUsed')}</Table.Th>
            <Table.Th />
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {(tokens.data ?? []).map((row) => (
            <Table.Tr key={row.id}>
              <Table.Td>
                <Text size="sm" ff="monospace">
                  {row.prefix}
                </Text>
              </Table.Td>
              <Table.Td>{dayjs(row.expiresAt).format('L')}</Table.Td>
              <Table.Td>{row.lastUsedAt ? dayjs(row.lastUsedAt).fromNow() : t('agents.never')}</Table.Td>
              <Table.Td>
                {row.revokedAt ? (
                  <Badge color="gray">{t('agents.revoked')}</Badge>
                ) : (
                  <Button size="compact-xs" variant="subtle" color="red" onClick={() => revoke.mutate(row.id)}>
                    {t('agents.revoke')}
                  </Button>
                )}
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Modal>
  );
}

function AuditCard({ rows }: { rows: Array<{ id: number; at: string; clientName: string | null; action: string; outcome: string; statusCode: number; sourceIp: string | null; durationMs: number }> }) {
  const { t } = useTranslation();
  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Title order={5}>{t('agents.audit')}</Title>
        <Text size="xs" c="dimmed">
          {t('agents.auditText')}
        </Text>
      </Group>
      {rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t('agents.auditEmpty')}
        </Text>
      ) : (
        <ScrollArea h={320}>
          <Table highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('agents.when')}</Table.Th>
                <Table.Th>{t('agents.client')}</Table.Th>
                <Table.Th>{t('agents.action')}</Table.Th>
                <Table.Th>{t('agents.outcome')}</Table.Th>
                <Table.Th>{t('agents.ip')}</Table.Th>
                <Table.Th>{t('agents.duration')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((row) => (
                <Table.Tr key={row.id}>
                  <Table.Td>{dayjs(row.at).format('HH:mm:ss')}</Table.Td>
                  <Table.Td>{row.clientName ?? '—'}</Table.Td>
                  <Table.Td>
                    <Text size="sm" ff="monospace">
                      {row.action}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Badge color={row.outcome === 'ok' ? 'green' : row.outcome === 'denied' ? 'yellow' : 'red'}>
                      {row.outcome} {row.statusCode}
                    </Badge>
                  </Table.Td>
                  <Table.Td>{row.sourceIp ?? '—'}</Table.Td>
                  <Table.Td>{row.durationMs} ms</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Card>
  );
}

function riskColor(risk: string): string {
  return risk === 'critical' ? 'red' : risk === 'high' ? 'orange' : risk === 'medium' ? 'yellow' : 'green';
}
