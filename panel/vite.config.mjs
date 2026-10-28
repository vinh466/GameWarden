import { tanstackRouter } from '@tanstack/router-plugin/vite';
import react from '@vitejs/plugin-react';
import { msw } from 'msw/vite';
import { oxfmt, oxlint } from 'oxc-config-mantine';
import { defineConfig, lazyPlugins } from 'vite-plus';

// Follows Mantine's Vite template (Vite+, oxlint, oxfmt) plus TanStack Router's file-based routing (docs/11 §7).
// `npm run dev` proxies the API to a development Core; `npm run dev:mock` answers it with MSW instead (src/mocks).
const core = process.env.GAMEWARDEN_URL ?? 'http://127.0.0.1:8765';
const generated = ['src/routeTree.gen.ts', 'src/api/generated'];

export default defineConfig({
  fmt: {
    ...oxfmt,
    ignorePatterns: [
      ...oxfmt.ignorePatterns,
      'dist',
      ...generated,
      '*.html',
      '*.yml',
      '*.json',
      '*.css',
    ],
  },
  lint: {
    ...oxlint,
    ignorePatterns: ['**/*.{mjs,cjs,js,d.ts,d.mts}', 'dist', ...generated],
    options: { typeAware: true, typeCheck: true },
  },
  plugins: lazyPlugins(() => [
    tanstackRouter({ target: 'react', autoCodeSplitting: true }),
    react(),
    msw(),
  ]),
  server: {
    port: 5173,
    proxy: {
      '/api': core,
      '/healthz': core,
      '/hubs': { target: core, ws: true },
    },
  },
  build: {
    // Core serves dist/ from wwwroot (docs/11 §12).
    outDir: 'dist',
    sourcemap: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './vitest.setup.mjs',
  },
  resolve: {
    tsconfigPaths: true,
  },
});
