import '@mantine/core/styles.css';
import '@mantine/charts/styles.css';
import '@mantine/dates/styles.css';
import '@mantine/notifications/styles.css';
import '@mantine/nprogress/styles.css';
import '@mantine/spotlight/styles.css';
import 'mantine-datatable/styles.css';

import { StrictMode } from 'react';
import '@fontsource-variable/inter';
import '@fontsource-variable/jetbrains-mono';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import './i18n';
import './styles/global.css';

/** `npm run dev:mock`: the API is answered in the browser by MSW (src/mocks), no Core needed. */
async function enableMocking() {
  if (import.meta.env.DEV && import.meta.env.MODE === 'mock') {
    const { network } = await import('virtual:msw');
    const { handlers } = await import('./mocks/handlers');
    network.configure({ handlers });
    await network.enable();
  }
}

void enableMocking().then(() => {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>
  );
});
