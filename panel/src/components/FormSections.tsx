import { useRef, type ReactNode } from 'react';
import { IconCheck, IconExclamationMark } from '@tabler/icons-react';
import { useTranslation } from 'react-i18next';
import { Accordion, Group, Text, ThemeIcon, VisuallyHidden } from '@mantine/core';
import { useReducedMotion } from '@mantine/hooks';
import type { SectionFlow, SectionStatus } from './sectionFlow';
import classes from './FormSections.module.css';

export interface FormSection {
  id: string;
  title: string;
  /** Under the title while the section is closed, so a choice is checked without reopening it (Baymard #648). */
  summary?: ReactNode;
  /** The section holds a field with a validation error. */
  invalid?: boolean;
  /** A control beside the title, outside the section's button (e.g. a `HelpButton`). */
  action?: ReactNode;
  children: ReactNode;
}

type FormSectionsProps = { sections: FormSection[] } & (
  | { flow: SectionFlow }
  | { open: string[]; onOpenChange: (open: string[]) => void }
);

/**
 * A form in sections on Mantine's Accordion (docs/11 §4, Owner review 2026-10-28). Guided (`flow`): one section is
 * open, the ones before it are summed up, the ones after it are locked until reached; the Owner moves with Next and
 * Back in a `FormFooter`, and the section that opens scrolls to the top and takes the focus. Free (`open`): any
 * section opens, for editing.
 */
export function FormSections(props: FormSectionsProps) {
  const reduceMotion = useReducedMotion();
  const items = useRef(new Map<string, HTMLDivElement>());
  const guided = 'flow' in props ? props.flow : null;
  const isOpen = (id: string, index: number) =>
    guided ? guided.active === index : 'open' in props && props.open.includes(id);

  const reveal = (id: string) => {
    const item = items.current.get(id);
    item?.scrollIntoView({ block: 'start', behavior: reduceMotion ? 'auto' : 'smooth' });
    item?.querySelector('button')?.focus({ preventScroll: true });
  };

  const body = props.sections.map((section, index) => {
    const open = isOpen(section.id, index);
    const status = guided?.status(index);
    // A guided section not completed yet has nothing chosen to sum up.
    const summary = !open && (!status || status === 'done') ? section.summary : null;
    const control = (
      <Accordion.Control
        disabled={status === 'locked'}
        icon={<SectionIcon index={index} status={status} invalid={section.invalid} />}
      >
        <SectionTitle section={section} status={status} />
        {summary && (
          <Text component="span" display="block" size="sm" c="dimmed" truncate="end">
            {summary}
          </Text>
        )}
      </Accordion.Control>
    );
    return (
      <Accordion.Item
        key={section.id}
        value={section.id}
        className={classes.item}
        ref={(node) => {
          if (node) {
            items.current.set(section.id, node);
          }
        }}
      >
        {section.action ? (
          // Mantine's "custom control" pattern: an action beside Accordion.Control, never inside its button.
          <Group gap={0} wrap="nowrap" pr="md">
            {control}
            {section.action}
          </Group>
        ) : (
          control
        )}
        <Accordion.Panel onTransitionEnd={guided && open ? () => reveal(section.id) : undefined}>
          {section.children}
        </Accordion.Panel>
      </Accordion.Item>
    );
  });

  if ('flow' in props) {
    const { flow, sections } = props;
    return (
      <Accordion
        variant="separated"
        order={3}
        disableCollapse
        value={sections[flow.active]?.id ?? null}
        onChange={(id) => flow.open(sections.findIndex((section) => section.id === id))}
      >
        {body}
      </Accordion>
    );
  }

  return (
    <Accordion
      variant="separated"
      order={3}
      multiple
      value={props.open}
      onChange={props.onOpenChange}
    >
      {body}
    </Accordion>
  );
}

function SectionTitle({ section, status }: { section: FormSection; status?: SectionStatus }) {
  const { t } = useTranslation();
  const state = section.invalid
    ? t('sections.invalid')
    : status === 'done'
      ? t('sections.done')
      : null;
  return (
    <Text component="span" display="block" fw={600}>
      {section.title}
      {state && <VisuallyHidden>{` (${state})`}</VisuallyHidden>}
    </Text>
  );
}

/** Number, check or exclamation mark: the state is in the shape as well as the colour (docs/11 §10 rule 8). */
function SectionIcon({
  index,
  status,
  invalid,
}: {
  index: number;
  status?: SectionStatus;
  invalid?: boolean;
}) {
  if (invalid) {
    return (
      <ThemeIcon size={28} radius="xl" color="red" variant="light">
        <IconExclamationMark size={16} aria-hidden />
      </ThemeIcon>
    );
  }

  if (!status) {
    return null;
  }

  if (status === 'done') {
    return (
      <ThemeIcon size={28} radius="xl" color="green" variant="light">
        <IconCheck size={16} aria-hidden />
      </ThemeIcon>
    );
  }

  return (
    <ThemeIcon
      size={28}
      radius="xl"
      color={status === 'current' ? 'violet' : 'gray'}
      variant={status === 'current' ? 'filled' : 'light'}
    >
      <Text component="span" size="sm" fw={600} aria-hidden>
        {index + 1}
      </Text>
    </ThemeIcon>
  );
}

/** Actions that stay in view at the bottom of a scrolling drawer or page, as Drawer.Header does at the top. */
export function FormFooter({ children }: { children: ReactNode }) {
  return <div className={classes.footer}>{children}</div>;
}
