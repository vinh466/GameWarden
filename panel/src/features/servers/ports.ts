import type { InstanceSnapshot } from '@/api/generated';

/** "2456–2457" for consecutive ports, otherwise a comma list. */
export function portsLabel(server: InstanceSnapshot): string {
  const ports = Object.values(server.ports).sort((a, b) => a - b);
  if (ports.length === 0) {
    return '';
  }

  const consecutive = ports.every(
    (port, index) => index === 0 || port === (ports[index - 1] ?? port) + 1
  );
  return consecutive && ports.length > 1
    ? `${ports[0]}–${ports[ports.length - 1]}`
    : ports.join(', ');
}

/** What friends type in the game: the address they reach the panel at (the VPN address) and the game port. */
export function connectAddress(server: InstanceSnapshot): string | null {
  const game = server.ports.game ?? Object.values(server.ports)[0];
  return game ? `${window.location.hostname}:${game}` : null;
}
