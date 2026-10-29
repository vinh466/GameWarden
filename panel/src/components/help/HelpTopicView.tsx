import type { ReactNode } from 'react';
import { IconAlertTriangle, IconBulb } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Anchor,
  Badge,
  List,
  Stack,
  Table,
  Text,
  Title,
  VisuallyHidden,
} from '@mantine/core';
import type { Block, HelpTopic } from './types';

export interface RelatedLink {
  id: string;
  title: string;
}

/**
 * A topic's parts in the order of docs/12 §3: in short, body, values, what we tested, related topics, and the
 * sources always last. Plain Mantine components; topic text is English and never HTML (docs/11 §10.7).
 */
export function HelpTopicView({
  topic,
  defaultValue,
  valueLabels,
  related = [],
  onOpenRelated,
}: {
  topic: HelpTopic;
  defaultValue?: string;
  /** A label per value, such as an enum's option labels; the value itself then shows below it. */
  valueLabels?: Record<string, string>;
  related?: RelatedLink[];
  onOpenRelated?: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <Stack gap="lg">
      <Part title={t('help.inShort')}>
        <Text size="sm">{topic.summary}</Text>
      </Part>
      {topic.body.length > 0 && (
        <Stack gap="sm">
          {topic.body.map((block, index) => (
            // Blocks have no ids and never move: the index is their identity.
            <BlockView key={index} block={block} />
          ))}
        </Stack>
      )}
      {topic.values && topic.values.length > 0 && (
        <Table withTableBorder layout="fixed">
          <Table.Thead>
            <Table.Tr>
              <Table.Th w="35%">{t('help.value')}</Table.Th>
              <Table.Th>{t('help.meaning')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {topic.values.map((row) => (
              <Table.Tr key={row.value}>
                <Table.Td>
                  <Stack gap={4} align="flex-start">
                    <Text size="sm" fw={500}>
                      {valueLabels?.[row.value] ?? row.value}
                    </Text>
                    {valueLabels?.[row.value] && valueLabels[row.value] !== row.value && (
                      <Text size="xs" c="dimmed" ff="monospace">
                        {row.value}
                      </Text>
                    )}
                    {row.value === defaultValue && (
                      <Badge size="xs" variant="outline" color="gray">
                        {t('help.default')}
                      </Badge>
                    )}
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{row.text}</Text>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      {topic.tested && (
        <Part title={t('help.tested')}>
          <Text size="sm">{topic.tested}</Text>
        </Part>
      )}
      {related.length > 0 && (
        <Part title={t('help.related')}>
          <List size="sm" spacing={4}>
            {related.map((link) => (
              <List.Item key={link.id}>
                <Anchor
                  component="button"
                  type="button"
                  size="sm"
                  onClick={() => onOpenRelated?.(link.id)}
                >
                  {link.title}
                </Anchor>
              </List.Item>
            ))}
          </List>
        </Part>
      )}
      <Part title={t('help.sources')}>
        <List size="sm" spacing="xs">
          {topic.sources.map((source) => (
            <List.Item key={source.url}>
              <Anchor href={source.url} target="_blank" rel="noreferrer" size="sm">
                {source.title}
                <VisuallyHidden> {t('help.newTab')}</VisuallyHidden>
              </Anchor>
              <Text size="xs" c="dimmed">
                {[source.publisher, t('help.checked', { date: source.checked })]
                  .filter(Boolean)
                  .join(' · ')}
              </Text>
            </List.Item>
          ))}
        </List>
      </Part>
    </Stack>
  );
}

function Part({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Stack gap={4}>
      <Title order={3} size="h6">
        {title}
      </Title>
      {children}
    </Stack>
  );
}

function BlockView({ block }: { block: Block }) {
  const { t } = useTranslation();
  if ('p' in block) {
    return <Text size="sm">{block.p}</Text>;
  }

  if ('list' in block) {
    return (
      <List size="sm" spacing={4}>
        {block.list.map((item) => (
          <List.Item key={item}>{item}</List.Item>
        ))}
      </List>
    );
  }

  if ('tip' in block) {
    return (
      <Alert color="violet" variant="light" p="sm" icon={<IconBulb size={18} aria-hidden />}>
        <Text size="sm">
          <VisuallyHidden>{`${t('help.tip')}: `}</VisuallyHidden>
          {block.tip}
        </Text>
      </Alert>
    );
  }

  return (
    <Alert color="yellow" variant="light" p="sm" icon={<IconAlertTriangle size={18} aria-hidden />}>
      <Text size="sm">
        <VisuallyHidden>{`${t('help.warning')}: `}</VisuallyHidden>
        {block.warning}
      </Text>
    </Alert>
  );
}
