import type { InstanceSnapshot } from '@/api/generated';
import { portsLabel } from './ports';

const server = (ports: Record<string, number>) => ({ ports }) as InstanceSnapshot;

describe('portsLabel', () => {
  it('shows consecutive ports as a range', () => {
    expect(portsLabel(server({ query: 2457, game: 2456 }))).toBe('2456–2457');
  });

  it('lists ports that are not consecutive', () => {
    expect(portsLabel(server({ game: 2456, rcon: 25575 }))).toBe('2456, 25575');
  });

  it('is empty without ports', () => {
    expect(portsLabel(server({}))).toBe('');
  });
});
