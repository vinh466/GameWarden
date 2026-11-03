// Pull-request screenshots (docs/11 §10 rule 10; the ux-playbook skill's visual check): each page at 1280 px and
// 390 px wide, in the light and the dark scheme.
//
// Start the mock panel first (`npm run dev:mock`), then:
//   npm run shots                                   the main screens
//   npm run shots -- /servers/valheim-main/mods     your own paths (mock ids are in src/mocks/data.ts)
//
//   GW_BASE             panel URL (default http://localhost:5173; the dev box is shared, another port may be in use)
//   GW_OUT              output folder (default shots/, ignored by git)
//   GW_BROWSER_CHANNEL  installed browser channel for playwright-core (default chrome; msedge on Windows)
//   GW_BROWSER_PATH     a Chromium-based browser executable instead; wins over the channel
//   GW_TIMEOUT          milliseconds to wait for each page to render and settle (default 60000; raise it on a busy box)
// Uses an installed browser through playwright-core; no browser download.
import { mkdirSync } from 'node:fs';
import { chromium } from 'playwright-core';

const base = (process.env.GW_BASE ?? 'http://localhost:5173').replace(/\/$/, '');
const out = process.env.GW_OUT ?? 'shots';
const paths = process.argv.slice(2);
const pages = paths.length
  ? paths
  : ['/', '/servers', '/servers/valheim-main', '/servers/valheim-main/mods', '/jobs', '/system'];
const widths = [1280, 390];
const schemes = ['light', 'dark'];
const timeout = Number(process.env.GW_TIMEOUT ?? 60_000);

try {
  await fetch(base);
} catch {
  console.error(`No panel at ${base}. Start it with \`npm run dev:mock\` or set GW_BASE.`);
  process.exit(1);
}

const installed = process.env.GW_BROWSER_PATH
  ? { executablePath: process.env.GW_BROWSER_PATH }
  : {
      channel:
        process.env.GW_BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : 'chrome'),
    };
const browser = await chromium.launch({ ...installed, headless: true });
mkdirSync(out, { recursive: true });

// Waits until the app has rendered and its requests have settled. A fresh page loads twice in mock mode (MSW
// reloads once its service worker is registered), and a cold dev server may reload once more after Vite optimizes
// dependencies. A locator wait survives those reloads; the content is checked again after the network is idle.
// On a busy machine the service-worker hand-off can stall a page; a page that has not rendered is reloaded.
async function settle(page) {
  page.setDefaultTimeout(timeout);
  const content = page.locator('#root > *').first();
  for (let attempt = 1; ; attempt++) {
    try {
      await content.waitFor({ state: 'attached', timeout: timeout / 2 });
      await page.waitForLoadState('networkidle');
      if ((await content.count()) > 0) break;
    } catch (error) {
      if (attempt >= 3) throw error;
      await page.reload();
      continue;
    }
    if (attempt >= 3) throw new Error('#root is still empty after three tries');
  }
  await page.evaluate(async () => {
    await document.fonts.ready;
  });
}

// Warm-up: let a cold dev server finish its first build before any screenshot. A failure here is only a warning;
// each page below still waits for itself.
const warmup = await browser.newPage();
try {
  await warmup.goto(base, { timeout });
  await settle(warmup);
} catch (error) {
  console.log(`warn warm-up: ${error.message.split('\n')[0]}`);
}
await warmup.close();

let failed = 0;
for (const path of pages) {
  const slug = path.replaceAll(/[^\w-]+/g, '-').replaceAll(/^-+|-+$/g, '') || 'dashboard';
  for (const colorScheme of schemes) {
    for (const width of widths) {
      const context = await browser.newContext({ viewport: { width, height: 900 }, colorScheme });
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', (error) => errors.push(error.message));
      const file = `${out}/${slug}-${width}-${colorScheme}.png`;
      try {
        await page.goto(`${base}${path}`, { timeout });
        await settle(page);
        await page.screenshot({ path: file, fullPage: true });
        console.log(`ok   ${file}${errors.length ? `  (page errors: ${errors.join('; ')})` : ''}`);
      } catch (error) {
        failed++;
        console.log(`FAIL ${file}: ${error.message.split('\n')[0]}`);
      }
      await context.close();
    }
  }
}

await browser.close();
process.exit(failed ? 1 : 0);
