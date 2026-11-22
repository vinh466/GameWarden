import { render, screen, userEvent } from '@test-utils';
import { SectionPicker } from './SectionPicker';

const options = [
  { value: 'overview', label: 'Overview' },
  { value: 'logs', label: 'Logs' },
];

describe('SectionPicker', () => {
  it('shows the current section under a labelled control', () => {
    render(<SectionPicker value="logs" options={options} onChange={() => {}} />);

    expect(screen.getByRole('combobox', { name: 'Section' })).toHaveValue('Logs');
  });

  it('reports the section the user picks', async () => {
    const onChange = vi.fn();
    render(<SectionPicker value="logs" options={options} onChange={onChange} />);

    await userEvent.click(screen.getByRole('combobox', { name: 'Section' }));
    await userEvent.click(screen.getByRole('option', { name: 'Overview' }));

    expect(onChange).toHaveBeenCalledWith('overview');
  });
});
