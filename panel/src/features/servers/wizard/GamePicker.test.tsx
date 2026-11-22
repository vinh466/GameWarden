import { render, screen, userEvent } from '@test-utils';
import type { AdapterSummary } from '@/api/generated';
import { GamePicker } from './GamePicker';

const adapter = (id: string, name: string): AdapterSummary => ({
  id,
  name,
  version: '1.0.0',
  level: 'L2',
  isBuiltIn: true,
  sourceType: 'folder',
  ports: [],
  requiredSettings: [],
  requiredSecrets: [],
  prerequisites: [],
  minResources: null,
  recommendedResources: null,
  settingGroups: [],
  settings: [],
});

const adapters = [adapter('valheim', 'Valheim'), adapter('palworld', 'Palworld')];

describe('GamePicker', () => {
  it('is a radio group with the current game checked', () => {
    render(<GamePicker adapters={adapters} selected="valheim" onPick={() => {}} />);

    expect(screen.getByRole('radiogroup', { name: 'Pick the game to host.' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Valheim/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /Palworld/ })).not.toBeChecked();
  });

  it('reports the chosen game and marks the group when Next is pressed without one', async () => {
    const onPick = vi.fn();
    const { rerender } = render(<GamePicker adapters={adapters} onPick={onPick} />);

    await userEvent.click(screen.getByRole('radio', { name: /Palworld/ }));
    expect(onPick).toHaveBeenCalledWith(adapters[1]);

    rerender(<GamePicker adapters={adapters} onPick={onPick} missing />);
    expect(screen.getByText('Choose a game to continue.')).toBeInTheDocument();
  });
});
