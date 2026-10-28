import { defineConfig } from '@hey-api/openapi-ts';

// The API client comes from Core's OpenAPI document, which Core's build writes to openapi/ (docs/11 §9).
// Run `npm run gen:api` after an API change; CI fails when src/api/generated is stale.
export default defineConfig({
  input: './openapi/gamewarden-api.json',
  output: { path: 'src/api/generated' },
  plugins: [
    { name: '@hey-api/client-fetch', runtimeConfigPath: './src/api/client-config.ts' },
    '@hey-api/typescript',
    '@hey-api/sdk',
    '@tanstack/react-query',
  ],
});
