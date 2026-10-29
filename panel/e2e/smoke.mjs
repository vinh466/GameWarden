// End-to-end smoke test (docs/11 §11): the built panel served by a real Core with FakeGame.
// First-run setup → create a server with the wizard → start → its line in the live console → stop → sign out.
//
// Core must already run, isolation off, e.g. from a Release build with panel/dist in its wwwroot (CI job `e2e`):
//   GW_BASE        panel URL                  (default http://localhost:18765)
//   GW_TOKEN_FILE  that Core's CLI token file (for the setup link)
//   GW_FAKEGAME    path to FakeGame (FakeGame.exe on Windows)
//   GW_OUT         folder for screenshots when a step fails (optional)
//   GW_BROWSER_CHANNEL  installed browser channel for playwright-core (default msedge, as in CI; e.g. chrome)
//   GW_BROWSER_PATH     a Chromium-based browser executable instead (e.g. /usr/bin/google-chrome); wins over the channel
// Uses an installed browser through playwright-core; no browser download.
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';

const base = process.env.GW_BASE ?? 'http://localhost:18765';
const token = readFileSync(process.env.GW_TOKEN_FILE, 'utf8').trim();
const fakeGame = process.env.GW_FAKEGAME;
const out = process.env.GW_OUT;
const cli = { Authorization: `Bearer ${token}` };

for (let i = 0; ; i++) {
  try {
    if ((await fetch(`${base}/healthz`)).ok) break;
  } catch {}
  if (i > 120) throw new Error(`Core does not answer at ${base}`);
  await new Promise((resolve) => setTimeout(resolve, 500));
}

const installed = process.env.GW_BROWSER_PATH
  ? { executablePath: process.env.GW_BROWSER_PATH }
  : { channel: process.env.GW_BROWSER_CHANNEL ?? 'msedge' };
const browser = await chromium.launch({ ...installed, headless: true });
const page = await (await browser.newContext({ viewport: { width: 1280, height: 860 } })).newPage();
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));

async function step(name, action) {
  const started = Date.now();
  try {
    await action();
    console.log(`ok   ${name} (${Date.now() - started} ms)`);
  } catch (error) {
    console.log(`FAIL ${name}: ${error.message}`);
    if (out) {
      await page
        .screenshot({ path: `${out}/failed-${name.replaceAll(/\W+/g, '-')}.png` })
        .catch(() => {});
    }
    await browser.close();
    process.exit(1);
  }
}

await step('first-run setup', async () => {
  const { url } = await (await fetch(`${base}/api/v1/setup/url`, { headers: cli })).json();
  await page.goto(url.replace(/^http:\/\/localhost:\d+/, base), { waitUntil: 'networkidle' });
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill('correct horse battery');
  await page.getByRole('textbox', { name: 'Repeat the password' }).fill('correct horse battery');
  await page.getByRole('button', { name: 'Create the account' }).click();
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('button', { name: /Keep the defaults/ }).click();
  await page.waitForURL(`${base}/`);
});

await step('create a server with the wizard', async () => {
  await page.goto(`${base}/servers?new=true`, { waitUntil: 'networkidle' });
  const drawer = page.getByRole('dialog');
  await drawer.getByText('FakeGame (development and tests)').click();
  // By role and exact name: a label lookup would also match a help button's "About ‹label›" (docs/12).
  await drawer.getByRole('textbox', { name: 'Server name', exact: true }).fill('Smoke Test');
  await drawer.getByRole('button', { name: 'Next' }).click();
  await drawer.getByRole('textbox', { name: 'executable', exact: true }).fill(fakeGame);
  await drawer.getByRole('button', { name: 'Next' }).click();
  await drawer.getByRole('button', { name: 'Create & install' }).click();
  await page.waitForURL(/\/servers\/smoke-test\/jobs/);
});

await step('start it', async () => {
  await page.goto(`${base}/servers/smoke-test`, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByText('Running', { exact: true }).first().waitFor({ timeout: 30_000 });
});

await step('see its console live', async () => {
  await page.getByRole('tab', { name: 'Console' }).click();
  await page.getByText('FakeGame server ready').waitFor({ timeout: 30_000 });
});

await step('stop it', async () => {
  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await page.getByText('Stopped', { exact: true }).first().waitFor({ timeout: 30_000 });
});

await step('sign out', async () => {
  await page.getByRole('button', { name: 'Account' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await page.waitForURL(/\/login/);
});

await step('no errors in the page', async () => {
  if (errors.length > 0) {
    throw new Error(errors.join(' | '));
  }
});

await browser.close();
