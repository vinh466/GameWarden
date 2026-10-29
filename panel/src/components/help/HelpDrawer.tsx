import { useEffect, useRef, useState } from 'react';
import { IconArrowLeft } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import {
  Badge,
  Button,
  Drawer,
  getDefaultZIndex,
  Group,
  Skeleton,
  Stack,
  Text,
  useMantineTheme,
} from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { ProblemAlert } from '@/components/ProblemAlert';
import { HelpTopicView } from './HelpTopicView';
import type { HelpRequest } from './types';
import { useHelpTopic } from './useHelpTopic';

/**
 * The panel's one help drawer (docs/12 §3–4): on the right on desktop, from the bottom on phones, one layer above
 * Mantine's modals so it opens over the dialog that holds the trigger. Related topics replace the content and Back
 * returns; a second help drawer never opens.
 */
export function HelpDrawer({
  opened,
  history,
  onClose,
  onNavigate,
}: {
  opened: boolean;
  /** The topics shown, the current one last. */
  history: HelpRequest[];
  onClose: () => void;
  onNavigate: (history: HelpRequest[]) => void;
}) {
  const { t } = useTranslation();
  const theme = useMantineTheme();
  const phone = useMediaQuery(`(max-width: ${theme.breakpoints.sm})`);
  const request = history.at(-1);
  const { topic, title, valueLabels, related, isLoading, error, retry } = useHelpTopic(request);
  const body = useRef<HTMLDivElement>(null);
  const [moved, setMoved] = useState(0);

  // After Back or a related link the clicked control is gone: focus the first control of the new content.
  useEffect(() => {
    if (moved > 0) {
      body.current?.querySelector<HTMLElement>('button, a[href]')?.focus({ preventScroll: true });
    }
  }, [moved]);

  const navigate = (next: HelpRequest[]) => {
    onNavigate(next);
    setMoved((count) => count + 1);
  };

  const content = (() => {
    if (error) {
      return <ProblemAlert error={error} onRetry={retry} />;
    }

    if (isLoading) {
      return <Skeleton h={200} />;
    }

    if (!topic) {
      return (
        <Text size="sm" c="dimmed">
          {t('help.missing')}
        </Text>
      );
    }

    return (
      <HelpTopicView
        topic={topic}
        defaultValue={request?.defaultValue}
        valueLabels={valueLabels}
        related={related.map((link, index) => ({ id: String(index), title: link.title }))}
        onOpenRelated={(index) => {
          const link = related[Number(index)];
          if (link) {
            navigate([...history, link.request]);
          }
        }}
      />
    );
  })();

  return (
    <Drawer
      opened={opened}
      onClose={onClose}
      position={phone ? 'bottom' : 'right'}
      size={phone ? '85%' : 'md'}
      zIndex={getDefaultZIndex('modal') + 1}
      returnFocus
      title={request?.title ?? title ?? t('help.title')}
    >
      <Stack gap="md" ref={body}>
        {(history.length > 1 || (topic && !topic.verified)) && (
          <Group gap="xs">
            {history.length > 1 && (
              <Button
                variant="subtle"
                size="compact-sm"
                leftSection={<IconArrowLeft size={16} aria-hidden />}
                onClick={() => navigate(history.slice(0, -1))}
              >
                {t('help.back')}
              </Button>
            )}
            {topic && !topic.verified && <Badge color="yellow">{t('help.notVerified')}</Badge>}
          </Group>
        )}
        {content}
      </Stack>
    </Drawer>
  );
}
