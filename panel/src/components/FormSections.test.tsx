import { render, screen } from '@test-utils';
import userEvent from '@testing-library/user-event';
import { Button } from '@mantine/core';
import { FormSections, type FormSection } from './FormSections';
import { useSectionFlow } from './sectionFlow';

const sections: FormSection[] = [
  { id: 'game', title: 'Game', summary: 'Valheim', children: 'Pick a game' },
  { id: 'name', title: 'Name', summary: 'My server', children: 'Type a name' },
  { id: 'review', title: 'Review', children: 'All good' },
];

function Guided({ items = sections }: { items?: FormSection[] }) {
  const flow = useSectionFlow(items.length);
  return (
    <>
      <FormSections sections={items} flow={flow} />
      <Button onClick={flow.back}>Back</Button>
      <Button onClick={flow.next}>Next</Button>
    </>
  );
}

describe('FormSections', () => {
  it('opens the first section and locks the ones not reached', () => {
    render(<Guided />);
    expect(screen.getByRole('button', { name: /^Game/ })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: /^Name/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /^Review/ })).toBeDisabled();
    // Nothing chosen yet: no summaries.
    expect(screen.queryByText('My server')).not.toBeInTheDocument();
  });

  it('sums up a completed section and names its state in words', async () => {
    render(<Guided />);
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));

    const game = screen.getByRole('button', { name: /^Game/ });
    expect(game).toHaveAttribute('aria-expanded', 'false');
    expect(game).toHaveTextContent('Valheim');
    expect(game).toHaveTextContent('(completed)');
    expect(screen.getByRole('button', { name: /^Name/ })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: /^Review/ })).toBeDisabled();
  });

  it('goes back to a completed section by Back or by its header', async () => {
    render(<Guided />);
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('button', { name: /^Game/ })).toHaveAttribute('aria-expanded', 'true');

    await userEvent.click(screen.getByRole('button', { name: /^Name/ }));
    expect(screen.getByRole('button', { name: /^Name/ })).toHaveAttribute('aria-expanded', 'true');
  });

  it('marks a section with errors', () => {
    render(<Guided items={[{ ...sections[0]!, invalid: true }, ...sections.slice(1)]} />);
    expect(screen.getByRole('button', { name: /^Game/ })).toHaveTextContent('(needs a fix)');
  });

  it('opens any section when free, with summaries on the closed ones', () => {
    render(<FormSections sections={sections} open={['name']} onOpenChange={() => {}} />);
    expect(screen.getByRole('button', { name: /^Game/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: /^Game/ })).toHaveTextContent('Valheim');
    expect(screen.getByRole('button', { name: /^Name/ })).not.toHaveTextContent('My server');
  });
});
