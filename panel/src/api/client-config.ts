import type { CreateClientConfig } from './generated/client.gen';

// The panel is served by Core itself, so the API is on the same origin; the session cookie goes with every request.
export const createClientConfig: CreateClientConfig = (config) => ({
  ...config,
  baseUrl: '',
  credentials: 'same-origin',
});
