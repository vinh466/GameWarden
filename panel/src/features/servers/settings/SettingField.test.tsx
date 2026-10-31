import { QueryClient } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import type { GetInputPropsReturnType } from '@mantine/form';
import type { AdapterSetting } from '@/api/generated';
import { client } from '@/api/generated/client.gen';
import { queries } from '@/api/queries';
import { FormSections } from '@/components/FormSections';
import { HelpButton } from '@/components/help/HelpButton';
import { HelpTestProvider } from '@/components/help/HelpTestProvider';
import { adapters } from '@/mocks/data';
import { valheimHelp } from '@/mocks/valheimHelp';
import { valheimGroups, valheimSettings } from '@/mocks/valheimSettings';
import { SettingField } from './SettingField';
import { initialSettingValues } from './settingSchema';
import { useSettingSections } from './useSettingSections';

// docs/12: a setting or group with a topic in the adapter's help.json gets a help button; the drawer loads the
// adapter's topics once (GET /adapters/{id}/help), here from the mock API's data in the query cache.
const setting = (key: string) => {
  const found = valheimSettings.find((candidate) => candidate.key === key);
  if (!found) {
    throw new Error(`No Valheim setting ${key}`);
  }

  return found;
};

function seeded(withHelp = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  client.setQueryData(queries.adapters().queryKey, { adapters, problems: [] });
  if (withHelp) {
    client.setQueryData(queries.adapterHelp('valheim').queryKey, valheimHelp);
  }

  return client;
}

const input = (value: string) =>
  ({ value, onChange: () => undefined }) as unknown as GetInputPropsReturnType;

function renderField(key: string, value = '', secretSet?: boolean) {
  render(
    <HelpTestProvider client={seeded()}>
      <SettingField
        setting={setting(key)}
        adapterId="valheim"
        input={input(value)}
        secretSet={secretSet}
      />
    </HelpTestProvider>
  );
}

function renderCustom(custom: AdapterSetting, value: string) {
  render(
    <HelpTestProvider client={seeded(false)}>
      <SettingField setting={custom} input={input(value)} />
    </HelpTestProvider>
  );
}

describe('SettingField help', () => {
  it('has no help button without a topic', () => {
    expect(setting('serverName').hasHelp).toBe(false);
    renderField('serverName');
    expect(screen.getByRole('textbox', { name: 'Name in Valheim' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^About/ })).not.toBeInTheDocument();
  });

  // Every input type; a switch's name has always included its description (Mantine puts it in the label).
  const crossplay = 'Crossplay (Xbox, Game Pass, Microsoft Store)';
  it.each([
    ['world', 'textbox', 'World name', 'World name', ''],
    ['saveInterval', 'textbox', 'Save every (seconds)', 'Save every (seconds)', '600'],
    ['preset', 'combobox', 'Difficulty preset', 'Difficulty preset', 'normal'],
    ['password', null, 'Password', 'Password', ''],
    [
      'crossplay',
      'switch',
      crossplay,
      `${crossplay} ${setting('crossplay').help?.en ?? ''}`,
      'false',
    ],
  ] as const)(
    '%s: "About ‹label›" beside an input that keeps its name',
    (key, role, label, name, value) => {
      expect(setting(key).hasHelp).toBe(true);
      renderField(key, value);
      const button = screen.getByRole('button', { name: `About ${label}` });
      const field = role
        ? screen.getByRole(role, { name })
        : screen.getByLabelText(/^Password/, { selector: 'input' });
      expect(field).toHaveAccessibleName(name);
      for (const element of document.querySelectorAll('label')) {
        expect(element).not.toContainElement(button);
      }
    }
  );

  it('keeps a secret badge in the label and the button outside it', () => {
    renderField('password', '', true);
    expect(screen.getByLabelText(/^Password/, { selector: 'input' })).toHaveAccessibleName(
      'Password Set'
    );
    expect(screen.getByRole('button', { name: 'About Password' })).toBeInTheDocument();
  });

  it("opens the adapter's topic: option labels, the default marked, related topics", async () => {
    renderField('preset', 'hard');
    await userEvent.click(screen.getByRole('button', { name: 'About Difficulty preset' }));
    const drawer = await screen.findByRole('dialog', { name: 'Difficulty preset' });
    expect(within(drawer).getByText('Not verified yet')).toBeInTheDocument();

    const normal = within(drawer).getByText('Normal', { selector: 'p' }).closest('tr');
    expect(normal).not.toBeNull();
    expect(within(normal!).getByText('normal')).toBeInTheDocument();
    expect(within(normal!).getByText('default')).toBeInTheDocument();
    const hard = within(drawer).getByText('Hard', { selector: 'p' }).closest('tr');
    expect(within(hard!).queryByText('default')).not.toBeInTheDocument();

    await userEvent.click(within(drawer).getByRole('button', { name: 'World modifiers' }));
    expect(screen.getByRole('dialog')).toHaveAccessibleName('World modifiers');
    expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument();
  });

  it('says so when the adapter has no topic for the key', async () => {
    render(
      <HelpTestProvider client={seeded()}>
        <HelpButton label="Mystery" help={{ adapter: { id: 'valheim', key: 'mystery' } }} />
      </HelpTestProvider>
    );
    await userEvent.click(screen.getByRole('button', { name: 'About Mystery' }));
    expect(await screen.findByText('This explanation is missing.')).toBeInTheDocument();
  });

  it('shows the problem with a retry when the topics do not load', async () => {
    // An absolute base URL, so the generated client builds its Request outside a browser; the answer is a 404.
    client.setConfig({ baseUrl: 'http://localhost' });
    const fetch = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ title: "No adapter 'valheim'.", status: 404 }), {
        status: 404,
        headers: { 'content-type': 'application/problem+json' },
      })
    );
    render(
      <HelpTestProvider client={seeded(false)}>
        <HelpButton label="Crossplay" help={{ adapter: { id: 'valheim', key: 'crossplay' } }} />
      </HelpTestProvider>
    );
    await userEvent.click(screen.getByRole('button', { name: 'About Crossplay' }));
    expect(await screen.findByText("No adapter 'valheim'.")).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalled();
    fetch.mockRestore();
    client.setConfig({ baseUrl: '' });
  });
});

function Sections() {
  const fields = valheimSettings;
  const sections = useSettingSections({
    adapterId: 'valheim',
    groups: valheimGroups,
    fields,
    values: initialSettingValues(fields),
    errors: {},
    input: () => input(''),
  });
  return (
    <FormSections
      sections={sections}
      open={sections.map((section) => section.id)}
      onOpenChange={() => undefined}
    />
  );
}

describe('number and list inputs (14 §4.2)', () => {
  it('renders a number as a decimal NumberInput with its bounds and step', () => {
    renderCustom(
      {
        ...setting('saveInterval'),
        key: 'rate',
        type: 'number',
        min: 0.1,
        max: 20,
        step: 0.5,
        default: '1',
      },
      '2.5'
    );

    expect(screen.getByLabelText('Save every (seconds)')).toHaveValue('2.5');
  });

  it('renders a free quoted list as tags', () => {
    renderCustom(
      {
        ...setting('preset'),
        key: 'deny',
        type: 'list',
        quoted: true,
        default: '',
        options: [],
      },
      'PALBOX,RepairBench'
    );

    expect(screen.getByText('PALBOX')).toBeInTheDocument();
    expect(screen.getByText('RepairBench')).toBeInTheDocument();
  });

  it('renders a list as a multi-select, one pill per value', () => {
    renderCustom(
      {
        ...setting('preset'),
        key: 'platforms',
        type: 'list',
        default: 'Steam',
        options: [
          { value: 'Steam', label: { en: 'Steam' } },
          { value: 'Xbox', label: { en: 'Xbox' } },
        ],
      },
      'Steam,Xbox'
    );

    expect(screen.getAllByText('Steam').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Xbox').length).toBeGreaterThan(0);
  });
});

describe('settings group help', () => {
  it('puts the button beside the section title, outside its button, and opens group:<id>', async () => {
    render(
      <HelpTestProvider client={seeded()}>
        <Sections />
      </HelpTestProvider>
    );
    const button = screen.getByRole('button', { name: 'About World modifiers' });
    expect(button.parentElement?.closest('button')).toBeNull();
    // Groups without a topic have none.
    expect(screen.queryByRole('button', { name: 'About Server' })).not.toBeInTheDocument();

    await userEvent.click(button);
    const drawer = await screen.findByRole('dialog', { name: 'World modifiers' });
    await waitFor(() =>
      expect(within(drawer).getByRole('button', { name: 'Difficulty preset' })).toBeInTheDocument()
    );
  });
});
