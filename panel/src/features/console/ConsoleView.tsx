import dayjs from 'dayjs';
import { useEffect, useMemo, useRef, useState } from 'react';
import { IconArrowDown } from '@tabler/icons-react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useTranslation } from 'react-i18next';
import { Box, Button, Highlight, Text } from '@mantine/core';
import type { OutputLine } from '@/api/generated';
import classes from './ConsoleView.module.css';

/**
 * The game console (docs/11 §4): virtualised so thousands of lines stay smooth; follows new lines until the Owner
 * scrolls up, then offers "Jump to latest". stderr is red, GameWarden's own lines are dimmed.
 */
export function ConsoleView({
  lines,
  search,
  height,
}: {
  lines: OutputLine[];
  search: string;
  height: string | number;
}) {
  const { t } = useTranslation();
  const scroller = useRef<HTMLDivElement>(null);
  const [following, setFollowing] = useState(true);
  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return needle ? lines.filter((line) => line.text.toLowerCase().includes(needle)) : lines;
  }, [lines, search]);

  const virtualizer = useVirtualizer({
    count: shown.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => 21,
    overscan: 30,
  });

  useEffect(() => {
    if (following && shown.length > 0) {
      virtualizer.scrollToIndex(shown.length - 1, { align: 'end' });
    }
  }, [following, shown.length, virtualizer]);

  return (
    <Box pos="relative">
      <div
        ref={scroller}
        className={classes.scroller}
        style={{ height }}
        role="log"
        aria-live="polite"
        onScroll={(event) => {
          const element = event.currentTarget;
          setFollowing(element.scrollHeight - element.scrollTop - element.clientHeight < 32);
        }}
      >
        {shown.length === 0 ? (
          <Text c="dimmed" size="sm" p="md">
            {t('console.empty')}
          </Text>
        ) : (
          <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
            {virtualizer.getVirtualItems().map((item) => {
              const line = shown[item.index];
              if (!line) {
                return null;
              }

              return (
                <div
                  key={item.key}
                  data-index={item.index}
                  ref={virtualizer.measureElement}
                  className={classes.line}
                  data-stream={line.stream}
                  style={{ transform: `translateY(${item.start}px)` }}
                >
                  <span className={classes.time}>{dayjs(line.at).format('HH:mm:ss')}</span>
                  {search ? (
                    <Highlight component="span" highlight={search} className={classes.text}>
                      {line.text}
                    </Highlight>
                  ) : (
                    <span className={classes.text}>{line.text}</span>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
      {!following && (
        <Button
          size="xs"
          radius="xl"
          leftSection={<IconArrowDown size={14} />}
          className={classes.jump}
          onClick={() => setFollowing(true)}
        >
          {t('console.follow')}
        </Button>
      )}
    </Box>
  );
}
